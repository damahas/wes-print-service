using Microsoft.AspNetCore.Mvc;
using Wes.Print.Core.Api.Dtos;
using Wes.Print.Core.Messaging;
using Wes.Print.Core.Print;
using Wes.Print.Core.Storage;
using Wes.Print.Core.Storage.Entities;

namespace Wes.Print.Core.Api.Controllers;

/// <summary>
/// 打印服务 RESTful API：MQ 配置与连接控制、打印机、打印记录、通用开关、健康检查。
/// </summary>
[ApiController]
public class PrintServiceController : ControllerBase
{
    private readonly IStorage _storage;
    private readonly IPrinterProvider _printerProvider;
    private readonly MqConnectionManager _mq;
    private readonly IPrintEngine _printEngine;

    public PrintServiceController(IStorage storage, IPrinterProvider printerProvider, MqConnectionManager mq, IPrintEngine printEngine)
    {
        _storage = storage;
        _printerProvider = printerProvider;
        _mq = mq;
        _printEngine = printEngine;
    }

    #region 健康检查
    [Route("health")]
    [HttpGet]
    public IActionResult Health() => Ok(new { status = "ok" });
    #endregion

    #region 打印机
    [Route("api/printers")]
    [HttpGet]
    public IActionResult GetPrinters()
    {
        var printers = _printerProvider.GetPrinters();
        var def = _printerProvider.DefaultPrinterName;
        return Ok(new { defaultPrinter = def, printers });
    }
    #endregion

    #region MQ 配置
    [Route("api/mq/config")]
    [HttpGet]
    public async Task<IActionResult> GetMqConfig([FromQuery] string? key, CancellationToken ct)
    {
        var cfg = await _storage.GetMqConfigAsync(key ?? "default", ct);
        return Ok(cfg is null ? new MqConfigDto() : ToMqDto(cfg));
    }

    [Route("api/mq/config")]
    [HttpPost]
    public async Task<IActionResult> SaveMqConfig([FromBody] MqConfigDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Type))
            return BadRequest("Type 不能为空");

        var entity = new MqConfig
        {
            Key = dto.Key ?? "default",
            Type = dto.Type,
            Enabled = dto.Enabled,
            Host = dto.Host,
            Port = dto.Port,
            UserName = dto.UserName,
            Password = dto.Password,
            Queue = dto.Queue,
            GroupId = dto.GroupId,
            BootstrapServers = dto.BootstrapServers,
            AutoAck = dto.AutoAck,
            PrinterName = dto.PrinterName,
        };
        var saved = await _storage.SaveMqConfigAsync(entity, ct);
        return Ok(ToMqDto(saved));
    }
    #endregion

    #region MQ 连接状态 / 控制
    [Route("api/mq/status")]
    [HttpGet]
    public IActionResult GetMqStatus([FromServices] MqConnectionManager mq)
    {
        var connected = mq.State == MqConnectionState.Connected;
        return Ok(new
        {
            enabled = mq.State != MqConnectionState.Disabled,
            connected,
            state = mq.State.ToString(),
            message = mq.LastMessage ?? string.Empty,
        });
    }

    [Route("api/mq/connect")]
    [HttpPost]
    public async Task<IActionResult> MqConnect([FromServices] MqConnectionManager mq)
    {
        var (ok, error) = await mq.ConnectAsync();
        return ok
            ? Ok(new { ok = true, state = mq.State.ToString(), message = mq.LastMessage })
            : BadRequest(new { ok = false, error });
    }

    [Route("api/mq/disconnect")]
    [HttpPost]
    public async Task<IActionResult> MqDisconnect([FromServices] MqConnectionManager mq)
    {
        await mq.DisconnectAsync();
        return Ok(new { ok = true, state = mq.State.ToString(), message = mq.LastMessage });
    }
    #endregion

    #region 打印记录
    [Route("api/records")]
    [HttpGet]
    public async Task<IActionResult> GetRecords(
        [FromQuery] string? channel,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var items = await _storage.QueryPrintRecordsAsync(channel, status, page, pageSize, ct);
        var total = await _storage.CountPrintRecordsAsync(channel, status, ct);
        var result = new PagedResult<PrintRecordDto>
        {
            Items = items.Select(ToRecordDto).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        };
        return Ok(result);
    }

    [Route("api/records/{id:long}")]
    [HttpGet]
    public async Task<IActionResult> GetRecord([FromRoute] long id, CancellationToken ct = default)
    {
        var rec = await _storage.GetPrintRecordAsync(id, ct);
        if (rec is null) return NotFound();
        return Ok(ToRecordDto(rec));
    }

    /// <summary>
    /// 打印内容预览：根据记录的模板与提交参数渲染第一页 PNG，返回 base64 图像。
    /// 仅预览，不触发任何打印。
    /// </summary>
    [Route("api/records/{id:long}/preview")]
    [HttpGet]
    public async Task<IActionResult> PreviewRecord([FromRoute] long id, CancellationToken ct = default)
    {
        var rec = await _storage.GetPrintRecordAsync(id, ct);
        if (rec is null) return NotFound();

        List<Dictionary<string, string>>? fields = null;
        if (!string.IsNullOrWhiteSpace(rec.Request))
        {
            try
            {
                fields = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(rec.Request);
            }
            catch { /* 参数格式异常则按空数据预览 */ }
        }

        var message = new PrintMessage
        {
            TemplateKind = rec.TemplateKind ?? PrintMessage.TemplateKindTemplate,
            TemplateRef = rec.TemplateRef,
            Fields = fields ?? new List<Dictionary<string, string>>(),
        };

        try
        {
            var base64 = await _printEngine.RenderToPngBase64Async(message, ct);
            return Ok(new
            {
                id,
                contentType = "image/png",
                base64,
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "预览渲染失败：" + ex.Message });
        }
    }
    #endregion

    #region 通用开关：MQ 启用
    [Route("api/settings/mq-enabled")]
    [HttpGet]
    public async Task<IActionResult> GetMqEnabled(CancellationToken ct = default)
    {
        var v = await _storage.GetSettingAsync("mq.enabled", ct);
        return Ok(new { key = "mq.enabled", value = v ?? "false" });
    }

    [Route("api/settings/mq-enabled")]
    [HttpPost]
    public async Task<IActionResult> SetMqEnabled([FromBody] SettingDto dto, CancellationToken ct = default)
    {
        var enabled = dto.Value == "true";
        await _storage.SetSettingAsync(dto.Key ?? "mq.enabled", dto.Value, ct);
        // 启用/停用切换时联动连接状态：启用->自动连接（若配置完整），停用->断开
        await _mq.ApplyEnabledAsync(enabled);
        return Ok(new { key = dto.Key ?? "mq.enabled", value = dto.Value, enabled });
    }
    #endregion

    #region 打印记录保留策略
    /// <summary>保留天数 setting 键（默认 30 天）。超过该天数的记录将被自动/手动清理。</summary>
    private const string RetentionKey = "record.retention-days";

    [Route("api/settings/record-retention")]
    [HttpGet]
    public async Task<IActionResult> GetRetention(CancellationToken ct = default)
    {
        var v = await _storage.GetSettingAsync(RetentionKey, ct);
        var days = int.TryParse(v, out var d) && d > 0 ? d : 30;
        return Ok(new { key = RetentionKey, value = days });
    }

    [Route("api/settings/record-retention")]
    [HttpPost]
    public async Task<IActionResult> SetRetention([FromBody] SettingDto dto, CancellationToken ct = default)
    {
        if (!int.TryParse(dto.Value, out var days) || days <= 0)
            return BadRequest("保留天数必须为正整数");
        await _storage.SetSettingAsync(RetentionKey, days.ToString(), ct);
        return Ok(new { key = RetentionKey, value = days });
    }

    /// <summary>手动清理超过保留天数的记录，返回删除条数。</summary>
    [Route("api/records/purge")]
    [HttpPost]
    public async Task<IActionResult> PurgeRecords(CancellationToken ct = default)
    {
        var v = await _storage.GetSettingAsync(RetentionKey, ct);
        var days = int.TryParse(v, out var d) && d > 0 ? d : 30;
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var removed = await _storage.PurgeOldPrintRecordsAsync(cutoff, ct);
        return Ok(new { removed, retentionDays = days, cutoff });
    }
    #endregion

    #region 映射辅助
    private static MqConfigDto ToMqDto(MqConfig e) => new()
    {
        Key = e.Key,
        Type = e.Type,
        Enabled = e.Enabled,
        Host = e.Host,
        Port = e.Port,
        UserName = e.UserName,
        Password = e.Password,
        Queue = e.Queue,
        GroupId = e.GroupId,
        BootstrapServers = e.BootstrapServers,
        AutoAck = e.AutoAck,
        PrinterName = e.PrinterName,
    };

    private static PrintRecordDto ToRecordDto(PrintRecord e) => new()
    {
        Id = e.Id,
        Channel = e.Channel,
        Status = e.Status,
        Message = e.Message,
        TemplateKind = e.TemplateKind,
        TemplateRef = e.TemplateRef,
        PrinterName = e.PrinterName,
        SourceRef = e.SourceRef,
        Request = e.Request,
        CreatedAt = e.CreatedAt,
    };
    #endregion
}
