using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Wes.Print.Core.Print;
using Wes.Print.Core.Storage;

namespace Wes.Print.Core.Messaging;

/// <summary>
/// MQ 连接管理器（单例）：统一持有消费者生命周期、连接状态、配置校验与重连。
/// 供 API 查询状态、手动连接/断开；启动时若开关开启则自动连接。
/// </summary>
public class MqConnectionManager : IDisposable
{
    private readonly IServiceProvider _sp;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPrintMessageConsumer? _consumer;
    private CancellationTokenSource? _cts;
    private string? _printerName;   // 来自 MQ 配置，前端下拉选择的打印机

    public MqConnectionState State { get; private set; } = MqConnectionState.Disabled;
    public string? LastMessage { get; private set; }

    public event Action<MqConnectionState, string?>? StateChanged;

    public MqConnectionManager(IServiceProvider sp)
    {
        _sp = sp;
    }

    public void InitFromStorage()
    {
        // 启动时调用：根据开关与配置决定初始状态（不主动连接，等 StartAsync 或自动）
        using var scope = _sp.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorage>();
        var enabled = IsEnabledFromStorage(storage);
        var cfg = storage.GetMqConfigAsync("default").GetAwaiter().GetResult();

        if (!enabled)
            SetState(MqConnectionState.Disabled, "MQ 消费已手动禁用");
        else if (cfg is null || string.IsNullOrWhiteSpace(cfg.Host) || string.IsNullOrWhiteSpace(cfg.Queue))
            SetState(MqConnectionState.NoConfig, "未配置主机/队列");
        else
            SetState(MqConnectionState.Idle, "已启用，等待连接");
    }

    /// <summary>从存储判断 MQ 是否启用：setting 显式 "false" 或配置 Enabled=false 均视为禁用，其余启用。</summary>
    private static bool IsEnabledFromStorage(IStorage storage)
    {
        var enabledRaw = storage.GetSettingAsync("mq.enabled").GetAwaiter().GetResult();
        if (enabledRaw == "false") return false;
        var cfg = storage.GetMqConfigAsync("default").GetAwaiter().GetResult();
        if (cfg is not null && !cfg.Enabled) return false;
        return true;
    }

    public async Task StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_consumer is not null) return; // 已在运行

            var (opts, reason) = await BuildOptionsAsync();
            if (reason is not null)
            {
                SetState(State is MqConnectionState.Disabled ? MqConnectionState.Disabled : MqConnectionState.NoConfig, reason);
                return;
            }

            _printerName = opts.PrinterName;
            _cts = new CancellationTokenSource();
            _consumer = ConsumerFactory.Create(opts.Type, _sp);
            if (_consumer is RabbitMq.RabbitMqConsumer rmq)
                rmq.StateChanged += OnConsumerStateChanged;

            _consumer.Start(opts, OnMessageAsync, _cts.Token);
            // 具体 Connected 由 consumer 事件驱动；先保持当前（Connecting 由 consumer 设置）
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _cts?.Cancel();
            _cts = null;
            if (_consumer is RabbitMq.RabbitMqConsumer rmq)
                rmq.StateChanged -= OnConsumerStateChanged;
            _consumer?.Stop();
            _consumer?.Dispose();
            _consumer = null;
            SetState(MqConnectionState.Stopped, "已手动停止");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>手动连接：校验通过即启动消费者（含自动重连）。</summary>
    public async Task<(bool ok, string? error)> ConnectAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_consumer is not null)
                return (true, "已在连接/消费中");

            var (opts, reason) = await BuildOptionsAsync();
            if (reason is not null)
                return (false, reason);

            _printerName = opts.PrinterName;
            _cts = new CancellationTokenSource();
            _consumer = ConsumerFactory.Create(opts.Type, _sp);
            if (_consumer is RabbitMq.RabbitMqConsumer rmq)
                rmq.StateChanged += OnConsumerStateChanged;

            _consumer.Start(opts, OnMessageAsync, _cts.Token);
            return (true, null);
        }
        catch (Exception ex)
        {
            SetState(MqConnectionState.Failed, $"连接失败：{ex.Message}");
            return (false, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>手动断开：停止消费者，不再自动重连。</summary>
    public async Task DisconnectAsync()
    {
        await StopAsync();
    }

    /// <summary>
    /// 启用/停用开关切换时调用：
    /// - 停用（enabled=false）：断开消费者，状态置为 Disabled。
    /// - 启用（enabled=true）：若配置完整则自动连接（StartAsync），否则置为 NoConfig 等待配置。
    /// 是否真正自动连接取决于配置是否完整（与启动时一致）。
    /// </summary>
    public async Task ApplyEnabledAsync(bool enabled)
    {
        if (!enabled)
        {
            await StopAsync();
            SetState(MqConnectionState.Disabled, "MQ 消费已手动禁用");
            return;
        }

        // 启用：仅当当前未处于运行态时才尝试连接
        if (_consumer is not null) return;

        var (opts, reason) = await BuildOptionsAsync();
        if (reason is not null)
        {
            SetState(MqConnectionState.NoConfig, reason);
            return;
        }

        _printerName = opts.PrinterName;
        _cts = new CancellationTokenSource();
        _consumer = ConsumerFactory.Create(opts.Type, _sp);
        if (_consumer is RabbitMq.RabbitMqConsumer rmq)
            rmq.StateChanged += OnConsumerStateChanged;
        _consumer.Start(opts, OnMessageAsync, _cts.Token);
    }

    private void OnConsumerStateChanged(MqConnectionState state, string? message)
    {
        SetState(state, message);
    }

    private async Task OnMessageAsync(PrintMessage msg)
    {
        using var scope = _sp.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorage>();
        var executor = scope.ServiceProvider.GetRequiredService<PrintJobExecutor>();
        // 每次消费都读取最新 MQ 配置中的打印机，避免连接时缓存的旧值（如后台改了打印机后不生效）
        var printerName = (await storage.GetMqConfigAsync("default"))?.PrinterName ?? _printerName;
        // 基础校验通过即落库 Pending + 入队后台打印，MQ 侧立即返回（Ack），不阻塞消费线程，
        // 真实打印由 PrintQueue 统一串行执行，多个消息同时到达也不会并发抢打印机
        await executor.EnqueueAsync(msg, channel: "RabbitMQ", printerName: printerName, sourceRef: msg.MessageId, ct: CancellationToken.None);
    }

    private async Task<(ConsumerOptions opts, string? reason)> BuildOptionsAsync()
    {
        using var scope = _sp.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorage>();
        if (!IsEnabledFromStorage(storage))
            return (new ConsumerOptions(), "MQ 消费已手动禁用");

        var cfg = await storage.GetMqConfigAsync("default");
        if (cfg is null)
            return (new ConsumerOptions(), "尚未配置 MQ");

        if (!RabbitMq.RabbitMqConsumer.Validate(new ConsumerOptions
        {
            Host = cfg.Host,
            Port = cfg.Port,
            Queue = cfg.Queue,
        }, out var reason))
            return (new ConsumerOptions(), reason);

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
            AutoAck = cfg.AutoAck,
            PrinterName = cfg.PrinterName,
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
}
