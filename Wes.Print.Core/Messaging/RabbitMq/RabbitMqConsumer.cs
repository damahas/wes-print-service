using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Wes.Print.Core.Messaging.RabbitMq;

/// <summary>
/// RabbitMQ 消费者：真实连接 + 消费 + 自动重连（指数退避）。
/// 状态通过 State / StateChanged 对外暴露，供连接管理器与 API 使用。
/// 收到消息后反序列化为 PrintMessage 并回调 onMessage。
/// </summary>
public class RabbitMqConsumer : IPrintMessageConsumer
{
    private readonly object _lock = new();
    private IConnection? _connection;
    private IModel? _channel;
    private string? _consumerTag;
    private ConsumerOptions? _options;
    private Func<PrintMessage, Task>? _onMessage;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public MqConnectionState State { get; private set; } = MqConnectionState.Disconnected;

    /// <summary>状态变更事件（state, 描述信息）</summary>
    public event Action<MqConnectionState, string?>? StateChanged;

    public void Start(ConsumerOptions options, Func<PrintMessage, Task> onMessage, CancellationToken ct = default)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (onMessage is null) throw new ArgumentNullException(nameof(onMessage));

        lock (_lock)
        {
            if (_loopTask is { IsCompleted: false })
                throw new InvalidOperationException("消费者已在运行，请先 Stop。");

            _options = options;
            _onMessage = onMessage;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _loopTask = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        TryClose();
        lock (_lock)
        {
            _loopTask = null;
        }
        SetState(MqConnectionState.Stopped, "已停止");
    }

    /// <summary>校验配置是否能连接（不真正建连）。</summary>
    public static bool Validate(ConsumerOptions o, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(o.Host)) { reason = "主机(Host)为空"; return false; }
        if (string.IsNullOrWhiteSpace(o.Queue)) { reason = "队列(Queue)为空"; return false; }
        if (o.Port <= 0 || o.Port > 65535) { reason = "端口不合法"; return false; }
        reason = null;
        return true;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        // 重连退避：3s, 5s, 30s, 60s, 120s（封顶 120s）
        var backoff = new[] { 3000, 5000, 30000, 60000, 120000 };
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (attempt == 0)
                    SetState(MqConnectionState.Connecting, "正在连接 RabbitMQ...");
                else
                    SetState(MqConnectionState.Reconnecting, $"连接中断，第 {attempt} 次重连...");

                await ConnectOnceAsync(ct);

                // 连接成功，进入持续消费状态，直到连接断开或被取消
                SetState(MqConnectionState.Connected, "已连接，正在消费");
                attempt = 0;

                // 阻塞等待直到连接断开（ConnectionShutdown 触发）或取消
                var broken = await WaitForShutdownAsync(ct);
                if (ct.IsCancellationRequested)
                    break;

                if (broken)
                {
                    TryClose();
                    attempt++;
                    var delay = backoff[Math.Min(attempt, backoff.Length) - 1];
                    SetState(MqConnectionState.Reconnecting, $"连接中断，{delay / 1000}s 后重连...");
                    try { await Task.Delay(delay, ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) break;
                // 建连失败：等待退避后重试
                attempt++;
                var delay = backoff[Math.Min(attempt, backoff.Length) - 1];
                SetState(MqConnectionState.Reconnecting, $"连接失败：{Truncate(ex.Message)}，{delay / 1000}s 后重连");
                TryClose();
                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        TryClose();
        if (State is not (MqConnectionState.Stopped or MqConnectionState.Disabled))
            SetState(MqConnectionState.Disconnected, "连接已结束");
    }

    private Task ConnectOnceAsync(CancellationToken ct)
    {
        var o = _options!;
        var factory = new ConnectionFactory
        {
            HostName = o.Host,
            Port = o.Port,
            UserName = string.IsNullOrWhiteSpace(o.UserName) ? "guest" : o.UserName,
            Password = o.Password ?? "guest",
            VirtualHost = "/",
            DispatchConsumersAsync = true,
            // 关闭库自带自动恢复，由本类统一管理重连时序
            AutomaticRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
            // 显式心跳：客户端主动发心跳保活，避免空闲连接被 broker/网络设备回收。
            // 不设 SocketReadTimeout/SocketWriteTimeout —— 过短的读超时会在 broker 心跳间隔（默认 60s）
            // 内无数据时被触发，造成 "远程主机强迫关闭连接" 的误断。
            RequestedHeartbeat = TimeSpan.FromSeconds(30),
            ClientProvidedName = "Wes.PrintService",
        };

        _connection = factory.CreateConnection();
        _connection.ConnectionShutdown += OnConnectionShutdown;
        _channel = _connection.CreateModel();
        _channel.QueueDeclare(
            queue: o.Queue!,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnReceived;
        _consumerTag = _channel.BasicConsume(
            queue: o.Queue!,
            autoAck: o.AutoAck,
            consumer: consumer);
        return Task.CompletedTask;
    }

    private async Task OnReceived(object? sender, BasicDeliverEventArgs ea)
    {
        var o = _options!;
        try
        {
            var body = ea.Body.ToArray();
            var text = Encoding.UTF8.GetString(body);
            var msg = ParseMessage(text, ea.DeliveryTag.ToString());
            if (msg is not null && _onMessage is not null)
                await _onMessage(msg);

            if (!o.AutoAck && _channel is not null)
                _channel.BasicAck(ea.DeliveryTag, false);
        }
        catch (Exception)
        {
            // 解析/打印失败：非自动确认时拒绝并重新入队（避免死循环可改为不重入队）
            if (!o.AutoAck && _channel is not null)
            {
                try { _channel.BasicNack(ea.DeliveryTag, false, true); }
                catch { /* 连接已断则忽略 */ }
            }
        }
    }

    private static PrintMessage? ParseMessage(string text, string? deliveryTag)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        PrintMessage? msg = null;
        try
        {
            msg = JsonSerializer.Deserialize<PrintMessage>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch
        {
            // 非 JSON：消息体不是有效的 PrintMessage JSON 时，退化为空消息
            msg = null;
        }
        if (msg is not null && string.IsNullOrWhiteSpace(msg.MessageId))
            msg.MessageId = deliveryTag;
        return msg;
    }

    private void OnConnectionShutdown(object? sender, ShutdownEventArgs e)
    {
        // 仅标记，真正的重连由 RunLoop 的 WaitForShutdown 处理
        if (_cts is { IsCancellationRequested: false })
        {
            SetState(MqConnectionState.Reconnecting, $"连接断开：{Truncate(e.ReplyText)}");
        }
    }

    private Task<bool> WaitForShutdownAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>();
        if (_connection is null)
            return Task.FromResult(true);

        void Handler(object? s, ShutdownEventArgs e)
        {
            try { tcs.TrySetResult(true); } catch { }
        }

        _connection.ConnectionShutdown += Handler;
        ct.Register(() => tcs.TrySetResult(false));
        return tcs.Task;
    }

    private void TryClose()
    {
        try { if (_consumerTag is not null) _channel?.BasicCancel(_consumerTag); } catch { }
        _consumerTag = null;
        try { _channel?.Close(); } catch { }
        try { _channel?.Dispose(); } catch { }
        _channel = null;
        try { _connection?.Close(); } catch { }
        try { _connection?.Dispose(); } catch { }
        _connection = null;
    }

    private void SetState(MqConnectionState state, string? message)
    {
        State = state;
        StateChanged?.Invoke(state, message);
    }

    private static string Truncate(string? s, int max = 120)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
