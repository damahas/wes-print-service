using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Wes.Print.Core.Api.Dtos;
using Wes.Print.Core.Messaging;
using Wes.Print.Core.Print;
using Wes.Print.Core.Storage;

namespace Wes.Print.Core.Api.Controllers;

/// <summary>
/// 对外打印 API：供外部系统（如 WMS / ERP）直接提交打印任务，与管理后台 API 隔离。
/// 路径前缀 /api/external。
/// </summary>
[ApiController]
[Route("api/external")]
public class ExternalApiController : ControllerBase
{
    private readonly PrintJobExecutor _executor;
    private readonly IStorage _storage;

    public ExternalApiController(PrintJobExecutor executor, IStorage storage)
    {
        _executor = executor;
        _storage = storage;
    }

    #region 提交打印任务
    /// <summary>
    /// 提交一次打印任务：基础校验通过后立即落库 Pending 记录并返回 RecordId，
    /// 真实打印由后台队列（PrintQueue）异步串行执行，不阻塞调用方。
    /// 调用方可持 RecordId 通过 GET print/{id} 轮询结果。
    /// 对外提交打印任务接口。请求体字段与 MQ 消息（PrintMessage）结构一致。
    /// </summary>
    [Route("print")]
    [HttpPost]
    public async Task<IActionResult> SubmitPrintJob([FromBody] SubmitPrintJobDto dto, CancellationToken ct = default)
    {
        if (dto is null)
            return BadRequest(new { error = "请求体不能为空" });

        if (string.IsNullOrWhiteSpace(dto.TemplateRef))
            return BadRequest(new { error = "TemplateRef 不能为空（模板名 / 模板内容 / 文件链接）" });

        var message = new PrintMessage
        {
            TemplateKind = dto.TemplateKind,
            TemplateRef = dto.TemplateRef,
            Fields = dto.Fields ?? new List<Dictionary<string, string>>(),
        };

        // 打印机从 MQ 配置（前端下拉选择）取，不接收外部传入
        var mqCfg = await _storage.GetMqConfigAsync("default", ct);
        // 基础校验完成即入队，立即返回 RecordId（状态 Pending），不等打印完成
        var record = await _executor.EnqueueAsync(
            message, channel: "Api", printerName: mqCfg?.PrinterName, sourceRef: dto.SourceRef, ct: ct);

        return Ok(new SubmitPrintJobResultDto
        {
            RecordId = record.Id,
            Status = record.Status,
            Message = "已受理，后台队列打印中",
            PrinterName = record.PrinterName,
        });
    }
    #endregion

    #region 查询任务结果
    /// <summary>按记录 Id 查询提交过的打印任务结果。</summary>
    [Route("print/{id:long}")]
    [HttpGet]
    public async Task<IActionResult> GetPrintJob([FromRoute] long id, CancellationToken ct = default)
    {
        var rec = await _storage.GetPrintRecordAsync(id, ct);
        if (rec is null) return NotFound(new { error = "未找到该打印记录" });
        return Ok(new SubmitPrintJobResultDto
        {
            RecordId = rec.Id,
            Status = rec.Status,
            Message = rec.Message,
            PrinterName = rec.PrinterName,
        });
    }
    #endregion
}
