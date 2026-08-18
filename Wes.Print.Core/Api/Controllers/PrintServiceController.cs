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
    public async Task<IActionResult> GetPrinters(CancellationToken ct)
    {
        var printers = _printerProvider.GetPrinters();
        var saved = await _storage.GetSettingAsync(DefaultPrinterKey, ct);
        var def = string.IsNullOrWhiteSpace(saved) ? _printerProvider.DefaultPrinterName : saved;
        return Ok(new { defaultPrinter = def, printers });
    }

    /// <summary>默认打印机 setting 键。空值表示使用系统默认打印机。</summary>
    public const string DefaultPrinterKey = "printer.default";

    /// <summary>
    /// 读取默认打印机（与 MQ 配置解耦，独立存储）。
    /// 返回空字符串表示使用系统默认打印机。
    /// </summary>
    [Route("api/printer/default")]
    [HttpGet]
    public async Task<IActionResult> GetDefaultPrinter(CancellationToken ct = default)
    {
        var v = await _storage.GetSettingAsync(DefaultPrinterKey, ct);
        return Ok(new { key = DefaultPrinterKey, value = v ?? string.Empty });
    }

    /// <summary>保存默认打印机（空字符串=恢复系统默认）。</summary>
    [Route("api/printer/default")]
    [HttpPost]
    public async Task<IActionResult> SetDefaultPrinter([FromBody] SettingDto dto, CancellationToken ct = default)
    {
        var value = dto.Value ?? string.Empty;
        await _storage.SetSettingAsync(DefaultPrinterKey, value, ct);
        return Ok(new { key = DefaultPrinterKey, value });
    }
    #endregion

    #region MQ 配置
    [Route("api/mq/config")]
    [HttpGet]
    public async Task<IActionResult> GetMqConfig([FromQuery] string? key, CancellationToken ct)
    {
        var k = key ?? MqConnectionManager.RabbitMqKey;
        var cfg = await _storage.GetMqConfigAsync(k, ct);
        return Ok(cfg is null ? new MqConfigDto { Key = k } : ToMqDto(cfg));
    }

    [Route("api/mq/config")]
    [HttpPost]
    public async Task<IActionResult> SaveMqConfig([FromBody] MqConfigDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Type))
            return BadRequest("Type 不能为空");

        var entity = new MqConfig
        {
            Key = dto.Key ?? MqConnectionManager.RabbitMqKey,
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
        };
        var saved = await _storage.SaveMqConfigAsync(entity, ct);
        return Ok(ToMqDto(saved));
    }
    #endregion

    #region MQ 连接状态 / 控制
    [Route("api/mq/status")]
    [HttpGet]
    public async Task<IActionResult> GetMqStatus([FromServices] MqConnectionManager mq)
    {
        var items = await mq.GetAllStatusAsync();
        // 顶层兼容字段：任一通道已连接/启用即视为 true（供老前端逻辑参考）
        var anyConnected = System.Array.Exists(items, i => i.Connected);
        var anyEnabled = System.Array.Exists(items, i => i.Enabled);
        return Ok(new
        {
            enabled = anyEnabled,
            connected = anyConnected,
            state = anyConnected ? "Connected" : "Idle",
            message = string.Empty,
            items,
        });
    }

    [Route("api/mq/connect")]
    [HttpPost]
    public async Task<IActionResult> MqConnect([FromServices] MqConnectionManager mq, [FromQuery] string? key)
    {
        var k = key ?? MqConnectionManager.RabbitMqKey;
        var (ok, error) = await mq.ConnectAsync(k);
        var status = await mq.GetStatusAsync(k);
        return ok
            ? Ok(new { ok = true, key = k, state = status.State.ToString(), message = status.Message })
            : BadRequest(new { ok = false, key = k, error });
    }

    [Route("api/mq/disconnect")]
    [HttpPost]
    public async Task<IActionResult> MqDisconnect([FromServices] MqConnectionManager mq, [FromQuery] string? key)
    {
        var k = key ?? MqConnectionManager.RabbitMqKey;
        await mq.DisconnectAsync(k);
        var status = await mq.GetStatusAsync(k);
        return Ok(new { ok = true, key = k, state = status.State.ToString(), message = status.Message });
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

    #region 通用开关：MQ 启用（按通道 key 区分 rabbitmq / kafka）
    [Route("api/settings/mq-enabled")]
    [HttpGet]
    public async Task<IActionResult> GetMqEnabled([FromQuery] string? key, CancellationToken ct = default)
    {
        var k = key ?? MqConnectionManager.RabbitMqKey;
        var settingKey = $"mq.enabled.{k}";
        var v = await _storage.GetSettingAsync(settingKey, ct);
        return Ok(new { key = k, settingKey, value = v ?? "false" });
    }

    [Route("api/settings/mq-enabled")]
    [HttpPost]
    public async Task<IActionResult> SetMqEnabled([FromBody] SettingDto dto, [FromQuery] string? key, CancellationToken ct = default)
    {
        var k = key ?? dto.Key ?? MqConnectionManager.RabbitMqKey;
        var settingKey = $"mq.enabled.{k}";
        var enabled = dto.Value == "true";
        await _storage.SetSettingAsync(settingKey, dto.Value, ct);
        // 启用/停用切换时联动该通道连接状态：启用->自动连接（若配置完整），停用->断开
        await _mq.ApplyEnabledAsync(k, enabled);
        return Ok(new { key = k, settingKey, value = dto.Value, enabled });
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
