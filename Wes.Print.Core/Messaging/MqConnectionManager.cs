using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Wes.Print.Core.Api.Controllers;
using Wes.Print.Core.Print;
using Wes.Print.Core.Storage;

namespace Wes.Print.Core.Messaging;

/// <summary>
/// MQ 连接管理器（单例）：同时持有 RabbitMQ 与 Kafka 两条独立通道，
/// 各自拥有独立的连接生命周期、状态与自动重连，可同时启用、同时消费。
/// 供 API 查询状态、按通道手动连接/断开；启动时若对应开关开启则自动连接。
/// </summary>
public class MqConnectionManager : IDisposable
{
    /// <summary>两条通道的固定 key。</summary>
    public const string RabbitMqKey = "rabbitmq";
    public const string KafkaKey = "kafka";

    private readonly IServiceProvider _sp;
    private readonly MqChannel _rabbit;
    private readonly MqChannel _kafka;

    public MqConnectionManager(IServiceProvider sp)
    {
        _sp = sp;
        _rabbit = new MqChannel(sp, RabbitMqKey);
        _kafka = new MqChannel(sp, KafkaKey);
    }

    private MqChannel Channel(string key) =>
        key.Trim().ToLowerInvariant() switch
        {
            RabbitMqKey => _rabbit,
            KafkaKey => _kafka,
            _ => throw new ArgumentException($"不支持的 MQ key: {key}", nameof(key)),
        };

    public void InitFromStorage()
    {
        _rabbit.InitFromStorage();
        _kafka.InitFromStorage();
    }

    public async Task StartAsync()
    {
        await _rabbit.StartAsync();
        await _kafka.StartAsync();
    }

    /// <summary>仅启动指定通道（供启动时按各通道配置自动连接）。</summary>
    public Task StartAsync(string key) => Channel(key).StartAsync();

    public async Task StopAsync()
    {
        await _rabbit.StopAsync();
        await _kafka.StopAsync();
    }

    public Task<MqChannelStatus> GetStatusAsync(string key) => Channel(key).GetStatusAsync();

    public async Task<MqChannelStatus[]> GetAllStatusAsync()
    {
        return new[]
        {
            await _rabbit.GetStatusAsync(),
            await _kafka.GetStatusAsync(),
        };
    }

    public Task<(bool ok, string? error)> ConnectAsync(string key) => Channel(key).ConnectAsync();
    public Task DisconnectAsync(string key) => Channel(key).DisconnectAsync();
    public Task ApplyEnabledAsync(string key, bool enabled) => Channel(key).ApplyEnabledAsync(enabled);

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); } catch { }
        _rabbit.Dispose();
        _kafka.Dispose();
    }

    #region 单通道封装
    /// <summary>
    /// 单条 MQ 通道：独立持有消费者、令牌与状态，封装连接/断开/重连/状态事件。
    /// 状态变更通过 <see cref="StateChanged"/> 对外通知（管理器可转交 API）。
    /// </summary>
    private sealed class MqChannel : IDisposable
    {
        private readonly IServiceProvider _sp;
        private readonly string _key;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private IPrintMessageConsumer? _consumer;
        private CancellationTokenSource? _cts;
        private string? _printerName;

        public MqConnectionState State { get; private set; } = MqConnectionState.Disabled;
        public string? LastMessage { get; private set; }
        public event Action<MqConnectionState, string?>? StateChanged;

        public MqChannel(IServiceProvider sp, string key)
        {
            _sp = sp;
            _key = key;
        }

        private string ExpectedType => _key == KafkaKey ? "Kafka" : "RabbitMQ";

        public void InitFromStorage()
        {
            using var scope = _sp.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IStorage>();
            var cfg = storage.GetMqConfigAsync(_key).GetAwaiter().GetResult();
            var enabled = IsEnabledFromStorage(storage);

            if (!enabled)
                SetState(MqConnectionState.Disabled, "MQ 消费已手动禁用");
            else if (cfg is null)
                SetState(MqConnectionState.NoConfig, $"尚未配置 {ExpectedType}");
            else
            {
                var noConfig = _key == KafkaKey
                    ? string.IsNullOrWhiteSpace(cfg.BootstrapServers) || string.IsNullOrWhiteSpace(cfg.Queue) || string.IsNullOrWhiteSpace(cfg.GroupId)
                    : string.IsNullOrWhiteSpace(cfg.Host) || string.IsNullOrWhiteSpace(cfg.Queue);
                if (noConfig)
                    SetState(MqConnectionState.NoConfig, _key == KafkaKey ? "未配置 BootstrapServers/队列/消费组" : "未配置主机/队列");
                else
                    SetState(MqConnectionState.Idle, "已启用，等待连接");
            }
        }

        private bool IsEnabledFromStorage(IStorage storage)
        {
            var enabledRaw = storage.GetSettingAsync($"mq.enabled.{_key}").GetAwaiter().GetResult();
            if (enabledRaw == "false") return false;
            var cfg = storage.GetMqConfigAsync(_key).GetAwaiter().GetResult();
            if (cfg is not null && !cfg.Enabled) return false;
            return true;
        }

        public async Task StartAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (_consumer is not null) return;
                var (opts, reason) = await BuildOptionsAsync();
                if (reason is not null)
                {
                    SetState(State is MqConnectionState.Disabled ? MqConnectionState.Disabled : MqConnectionState.NoConfig, reason);
                    return;
                }
                StartConsumer(opts);
            }
            finally { _gate.Release(); }
        }

        public async Task StopAsync()
        {
            await _gate.WaitAsync();
            try
            {
                _cts?.Cancel();
                _cts = null;
                DetachConsumerEvents();
                _consumer?.Stop();
                _consumer?.Dispose();
                _consumer = null;
                SetState(MqConnectionState.Stopped, "已手动停止");
            }
            finally { _gate.Release(); }
        }

        public async Task<(bool ok, string? error)> ConnectAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (_consumer is not null) return (true, "已在连接/消费中");
                var (opts, reason) = await BuildOptionsAsync();
                if (reason is not null) return (false, reason);
                StartConsumer(opts);
                return (true, null);
            }
            catch (Exception ex)
            {
                SetState(MqConnectionState.Failed, $"连接失败：{ex.Message}");
                return (false, ex.Message);
            }
            finally { _gate.Release(); }
        }

        public async Task DisconnectAsync() => await StopAsync();

        public async Task ApplyEnabledAsync(bool enabled)
        {
            if (!enabled)
            {
                await StopAsync();
                SetState(MqConnectionState.Disabled, "MQ 消费已手动禁用");
                return;
            }
            if (_consumer is not null) return;
            var (opts, reason) = await BuildOptionsAsync();
            if (reason is not null)
            {
                SetState(MqConnectionState.NoConfig, reason);
                return;
            }
            StartConsumer(opts);
        }

        private void StartConsumer(ConsumerOptions opts)
        {
            _printerName = opts.PrinterName;
            _cts = new CancellationTokenSource();
            _consumer = CreateAndAttachConsumer(opts);
            _consumer.Start(opts, OnMessageAsync, _cts.Token);
        }

        private void DetachConsumerEvents()
        {
            switch (_consumer)
            {
                case RabbitMq.RabbitMqConsumer rmq:
                    rmq.StateChanged -= OnConsumerStateChanged;
                    break;
                case Kafka.KafkaConsumer kfk:
                    kfk.StateChanged -= OnConsumerStateChanged;
                    break;
            }
        }

        private void OnConsumerStateChanged(MqConnectionState state, string? message) => SetState(state, message);

        private IPrintMessageConsumer CreateAndAttachConsumer(ConsumerOptions opts)
        {
            var consumer = ConsumerFactory.Create(opts.Type, _sp);
            switch (consumer)
            {
                case RabbitMq.RabbitMqConsumer rmq:
                    rmq.StateChanged += OnConsumerStateChanged;
                    break;
                case Kafka.KafkaConsumer kfk:
                    kfk.StateChanged += OnConsumerStateChanged;
                    break;
            }
            return consumer;
        }

        private async Task OnMessageAsync(PrintMessage msg)
        {
            using var scope = _sp.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IStorage>();
            var executor = scope.ServiceProvider.GetRequiredService<PrintJobExecutor>();
            var printerName = (await storage.GetSettingAsync(PrintServiceController.DefaultPrinterKey)) ?? _printerName;
            await executor.EnqueueAsync(msg, channel: _key == KafkaKey ? "Kafka" : "RabbitMQ",
                printerName: printerName, sourceRef: msg.MessageId, ct: CancellationToken.None);
        }

        private async Task<(ConsumerOptions opts, string? reason)> BuildOptionsAsync()
        {
            using var scope = _sp.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IStorage>();
            if (!IsEnabledFromStorage(storage))
                return (new ConsumerOptions(), "MQ 消费已手动禁用");

            var cfg = await storage.GetMqConfigAsync(_key);
            if (cfg is null)
                return (new ConsumerOptions(), $"尚未配置 {ExpectedType}");

            var type = (cfg.Type ?? ExpectedType).Trim().ToLowerInvariant();
            if (type == "kafka")
            {
                if (!Kafka.KafkaConsumer.Validate(new ConsumerOptions
                {
                    BootstrapServers = cfg.BootstrapServers,
                    Queue = cfg.Queue,
                    GroupId = cfg.GroupId,
                }, out var reason))
                    return (new ConsumerOptions(), reason);
            }
            else
            {
                if (!RabbitMq.RabbitMqConsumer.Validate(new ConsumerOptions
                {
                    Host = cfg.Host,
                    Port = cfg.Port,
                    Queue = cfg.Queue,
                }, out var reason))
                    return (new ConsumerOptions(), reason);
            }

            var opts = new ConsumerOptions
            {
                Type = cfg.Type,
                Enabled = cfg.Enabled,
                Host = cfg.Host,
                Port = cfg.Port,
                UserName = cfg.UserName,
                Password = cfg.Password,
                Queue = cfg.Queue,
                GroupId = cfg.GroupId,
                BootstrapServers = cfg.BootstrapServers,
                AutoAck = cfg.AutoAck,
                PrinterName = await storage.GetSettingAsync(PrintServiceController.DefaultPrinterKey),
            };
            return (opts, null);
        }

        private void SetState(MqConnectionState state, string? message)
        {
            State = state;
            LastMessage = message;
            StateChanged?.Invoke(state, message);
        }

        public void Dispose()
        {
            try { StopAsync().GetAwaiter().GetResult(); } catch { }
        }

        public Task<MqChannelStatus> GetStatusAsync()
        {
            var enabled = State != MqConnectionState.Disabled;
            return Task.FromResult(new MqChannelStatus
            {
                Key = _key,
                Type = ExpectedType,
                Enabled = enabled,
                Connected = State == MqConnectionState.Connected,
                State = State.ToString(),
                Message = LastMessage ?? string.Empty,
            });
        }
    }
    #endregion

    /// <summary>单通道状态快照（供 API 返回）。</summary>
    public sealed record MqChannelStatus
    {
        public string Key { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public bool Enabled { get; init; }
        public bool Connected { get; init; }
        public string State { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }
}
