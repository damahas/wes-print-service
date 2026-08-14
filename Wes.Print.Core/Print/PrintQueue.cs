using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Wes.Print.Core.Messaging;
using Wes.Print.Core.Storage;
using Wes.Print.Core.Storage.Entities;

namespace Wes.Print.Core.Print;

/// <summary>
/// 后台打印队列（单例）：所有打印请求（对外 API / MQ 消费）只做"基础校验 + 落库 Pending 记录 + 入队"，
/// 立即返回 RecordId；真正的打印由本队列统一串行执行，避免并发抢打印机导致状态混乱。
/// 并发度通过 <see cref="MaxConcurrency"/> 控制（默认 1，即完全串行，适合单打印机场景）。
/// </summary>
public class PrintQueue : IDisposable
{
    private readonly IServiceProvider _sp;
    private readonly SemaphoreSlim _gate;
    private readonly ConcurrentQueue<QueuedItem> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _pumpLock = new();
    private int _running = 0;

    /// <summary>最大并发打印数。默认 1（串行）。多打印机可酌情调大。</summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>队列是否可用（未取消）。不可用时调用方应降级为同步打印。</summary>
    public bool IsAvailable => !_cts.IsCancellationRequested;

    public PrintQueue(IServiceProvider sp)
    {
        _sp = sp;
        _gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
    }

    /// <summary>
    /// 入队一条打印任务。调用方需先创建好 Pending 记录（record 含 Id），
    /// 队列取出后执行真实打印并更新状态。
    /// </summary>
    public void Enqueue(PrintRecord record, PrintMessage message, string? printerName)
    {
        _queue.Enqueue(new QueuedItem(record, message, printerName));
        Pump();
    }

    /// <summary>
    /// 触发执行：在不超过并发上限的前提下，尽快启动队列中的任务。
    /// 使用锁 + 计数保证不会超额启动。
    /// </summary>
    private void Pump()
    {
        lock (_pumpLock)
        {
            while (_running < MaxConcurrency && _queue.TryDequeue(out var item))
            {
                _running++;
                // 不阻塞调用线程：后台执行
                _ = _gate.WaitAsync(_cts.Token).ContinueWith(_ => RunItemAsync(item), TaskScheduler.Default);
            }
        }
    }

    private async Task RunItemAsync(QueuedItem item)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<PrintJobExecutor>();
            await executor.RunEnqueuedAsync(item.Record, item.Message, item.PrinterName, _cts.Token);
        }
        catch (Exception ex)
        {
            // 队列级兜底：保证记录不会永远 Pending
            try
            {
                using var scope = _sp.CreateScope();
                var storage = scope.ServiceProvider.GetRequiredService<IStorage>();
                item.Record.Status = "Failed";
                item.Record.Message = "队列执行异常：" + ex.Message;
                await storage.UpdatePrintRecordAsync(item.Record);
            }
            catch { /* 忽略存储异常，避免吞掉主异常 */ }
        }
        finally
        {
            _gate.Release();
            lock (_pumpLock)
            {
                _running--;
            }
            Pump(); // 任务结束后尝试启动下一个
        }
    }

    private sealed record QueuedItem(PrintRecord Record, PrintMessage Message, string? PrinterName);

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        _gate.Dispose();
        _cts.Dispose();
    }
}
