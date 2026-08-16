using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;

namespace Wes.Print.Core.Messaging.Kafka;

/// <summary>
/// Kafka 消费者：真实连接 + 消费 + 自动重连（指数退避，风格对齐 RabbitMqConsumer）。
/// 状态通过 State / StateChanged 对外暴露，供连接管理器与 API 使用。
/// 收到消息后反序列化为 PrintMessage 并回调 onMessage。
/// </summary>
public class KafkaConsumer : IPrintMessageConsumer
{
    private readonly object _lock = new();
    private IConsumer<Ignore, string>? _consumer;
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
        if (string.IsNullOrWhiteSpace(o.BootstrapServers)) { reason = "BootstrapServers 为空"; return false; }
        if (string.IsNullOrWhiteSpace(o.Queue)) { reason = "Topic 为空"; return false; }
        if (string.IsNullOrWhiteSpace(o.GroupId)) { reason = "消费组 GroupId 为空"; return false; }
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
                    SetState(MqConnectionState.Connecting, "正在连接 Kafka...");
                else
                    SetState(MqConnectionState.Reconnecting, $"连接中断，第 {attempt} 次重连...");

                await ConnectOnceAsync(ct);

                SetState(MqConnectionState.Connected, "已连接，正在消费");
                attempt = 0;

                // 持续拉取消息，直到取消或连接故障
                await ConsumeLoopAsync(ct);
                if (ct.IsCancellationRequested)
                    break;

                TryClose();
                attempt++;
                var delay = backoff[Math.Min(attempt, backoff.Length) - 1];
                SetState(MqConnectionState.Reconnecting, $"连接中断，{delay / 1000}s 后重连...");
                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { break; }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) break;
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
        var config = new ConsumerConfig
        {
            BootstrapServers = o.BootstrapServers,
            GroupId = o.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = o.AutoAck,        // 自动确认时由 SDK 自动提交位点
            EnablePartitionEof = true,
            // 合理超时：避免空闲时无限阻塞，同时留足 broker 心跳间隔
            SessionTimeoutMs = 30000,
            MaxPollIntervalMs = 300000,
            ClientId = "Wes.PrintService",
        };
        if (!string.IsNullOrWhiteSpace(o.UserName))
        {
            config.SecurityProtocol = SecurityProtocol.Plaintext;
            config.SaslUsername = o.UserName;
            config.SaslPassword = o.Password;
            config.SaslMechanism = SaslMechanism.Plain;
        }

        _consumer = new ConsumerBuilder<Ignore, string>(config).Build();
        _consumer.Subscribe(o.Queue!);   // Queue 字段在 Kafka 下作为 Topic 使用
        return Task.CompletedTask;
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 100ms 轮询，兼顾及时性与取消响应
                var result = _consumer!.Consume(TimeSpan.FromMilliseconds(100));
                if (result is null) continue;
                if (result.IsPartitionEOF) continue;

                var msg = ParseMessage(result.Message.Value, result.Message.Key?.ToString());
                if (msg is not null && _onMessage is not null)
                    await _onMessage(msg);

                if (!_options!.AutoAck && _consumer is not null)
                    _consumer.Commit(result);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException ex) when (ex.Error.IsFatal)
            {
                // 致命错误（如元数据获取失败、认证失败）：抛出触发外层重连
                throw;
            }
            catch (Exception)
            {
                // 单条解析/打印失败：继续消费下一条（Kafka 不阻塞，避免积压）
            }
        }
    }

    private static PrintMessage? ParseMessage(string text, string? key)
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
            msg = null;
        }
        if (msg is not null && string.IsNullOrWhiteSpace(msg.MessageId))
            msg.MessageId = key;
        return msg;
    }

    private void TryClose()
    {
        try { _consumer?.Close(); } catch { }
        try { _consumer?.Dispose(); } catch { }
        _consumer = null;
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
