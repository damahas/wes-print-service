using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wes.Print.Core.Messaging;
using Wes.Print.Core.Storage;
using Wes.Print.Core.Storage.Entities;

namespace Wes.Print.Core.Print;

/// <summary>
/// 打印任务执行器：统一的"提交一次打印"流程——落库记录 → 调用打印引擎 → 更新状态。
/// MQ 消费（MqConnectionManager）与对外 API（ExternalApiController）共用本服务，避免逻辑重复。
/// </summary>
public class PrintJobExecutor
{
    private readonly IPrintEngine _engine;
    private readonly IStorage _storage;
    private readonly IPrinterProvider _printerProvider;
    private readonly PrintQueue _queue;

    public PrintJobExecutor(IPrintEngine engine, IStorage storage, IPrinterProvider printerProvider, PrintQueue queue)
    {
        _engine = engine;
        _storage = storage;
        _printerProvider = printerProvider;
        _queue = queue;
    }

    /// <summary>
    /// 提交一条打印消息：基础校验 → 落库 Pending 记录 → 入队后台打印，立即返回记录（状态 Pending）。
    /// 真正的打印由 PrintQueue 串行执行，调用方无需等待打印完成。
    /// printerName 来自 MQ 配置（前端下拉选择）；为空则使用系统默认打印机。
    /// </summary>
    public async Task<PrintRecord> EnqueueAsync(
        PrintMessage message,
        string channel,
        string? printerName = null,
        string? sourceRef = null,
        CancellationToken ct = default)
    {
        var resolvedPrinter = string.IsNullOrWhiteSpace(printerName)
            ? _printerProvider.DefaultPrinterName
            : printerName;

        var record = new PrintRecord
        {
            Channel = channel,
            Status = "Pending",
            TemplateKind = message.TemplateKind,
            TemplateRef = message.TemplateRef,
            PrinterName = resolvedPrinter,
            SourceRef = sourceRef ?? message.MessageId,
            Request = SerializeRequest(message.Fields),
            CreatedAt = DateTime.UtcNow,
        };

        await _storage.AddPrintRecordAsync(record, ct);

        // 断网/服务关闭兜底：若队列已被取消，直接在调用线程降级同步打印
        if (_queue.IsAvailable)
            _queue.Enqueue(record, message, resolvedPrinter);
        else
            await RunEnqueuedAsync(record, message, resolvedPrinter, ct);

        return record;
    }

    /// <summary>
    /// 后台队列回调：执行真实打印并更新记录状态。供 PrintQueue 调用。
    /// </summary>
    public async Task RunEnqueuedAsync(
        PrintRecord record,
        PrintMessage message,
        string? printerName,
        CancellationToken ct = default)
    {
        try
        {
            await _engine.PrintAsync(message, printerName, ct);
            record.Status = "Success";
            record.Message = "打印完成";
        }
        catch (Exception ex)
        {
            record.Status = "Failed";
            record.Message = ex.Message;
        }

        await _storage.UpdatePrintRecordAsync(record, ct);
    }

    /// <summary>将提交参数（Fields）序列化为可读 JSON，便于审计查看。</summary>
    private static string? SerializeRequest(System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>? fields)
    {
        if (fields is null || fields.Count == 0) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(fields,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
        catch
        {
            return null;
        }
    }
}
