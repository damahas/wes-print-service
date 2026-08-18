using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text.Json;
using Wes.Print.Core.Print;
using Wes.Print.Core.Messaging;

namespace Wes.Print.Core.Api.Controllers;

/// <summary>
/// 打印模板管理：读写 PrintTemp 目录下的 .json 模板文件，并提供独立预览（不依赖数据库记录）。
/// </summary>
[ApiController]
public class TemplateController : ControllerBase
{
    private static readonly string PrintTempDir = ResolvePrintTempDir();

    private static string ResolvePrintTempDir()
    {
        var dllDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var deployed = Path.Combine(dllDir, "PrintTemp");
        if (Directory.Exists(deployed)) return deployed;
        var dir = dllDir;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "Wes.Print.Core", "PrintTemp");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }
        Directory.CreateDirectory(deployed);
        return deployed;
    }

    private static string SafeName(string name)
    {
        var s = name.Trim();
        if (string.IsNullOrWhiteSpace(s)) s = "template";
        // 仅允许安全文件名，避免路径穿越
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        if (!s.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) s += ".json";
        return s;
    }

    /// <summary>模板列表（PrintTemp 目录下所有 .json）。</summary>
    [Route("api/templates")]
    [HttpGet]
    public IActionResult List()
    {
        if (!Directory.Exists(PrintTempDir))
            return Ok(new { items = System.Array.Empty<object>() });
        var items = Directory.EnumerateFiles(PrintTempDir, "*.json", SearchOption.TopDirectoryOnly)
            .Select(p => new
            {
                name = Path.GetFileNameWithoutExtension(p),
                file = Path.GetFileName(p),
                size = new FileInfo(p).Length,
                updatedAt = new FileInfo(p).LastWriteTimeUtc,
            })
            .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Ok(new { items });
    }

    /// <summary>读取单个模板内容。</summary>
    [Route("api/templates/{name}")]
    [HttpGet]
    public IActionResult Get([FromRoute] string name)
    {
        var path = Path.Combine(PrintTempDir, SafeName(name));
        if (!System.IO.File.Exists(path)) return NotFound(new { error = "模板不存在：" + name });
        var json = System.IO.File.ReadAllText(path);
        return Content(json, "application/json; charset=utf-8");
    }

    /// <summary>保存（新建/覆盖）模板。</summary>
    [Route("api/templates/{name}")]
    [HttpPost]
    public IActionResult Save([FromRoute] string name, [FromBody] JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return BadRequest(new { error = "模板内容必须是 JSON 对象（含 page 与 items）。" });
        var path = Path.Combine(PrintTempDir, SafeName(name));
        var json = body.GetRawText();
        // 轻量校验：至少包含 page 与 items
        if (!body.TryGetProperty("page", out _) || !body.TryGetProperty("items", out _))
            return BadRequest(new { error = "模板缺少 page 或 items 字段。" });
        System.IO.File.WriteAllText(path, json);
        return Ok(new { name = Path.GetFileNameWithoutExtension(path), file = Path.GetFileName(path), saved = true });
    }

    /// <summary>删除模板。</summary>
    [Route("api/templates/{name}")]
    [HttpDelete]
    public IActionResult Delete([FromRoute] string name)
    {
        var path = Path.Combine(PrintTempDir, SafeName(name));
        if (!System.IO.File.Exists(path)) return NotFound(new { error = "模板不存在：" + name });
        System.IO.File.Delete(path);
        return Ok(new { name = Path.GetFileNameWithoutExtension(path), deleted = true });
    }

    /// <summary>
    /// 独立模板预览：接收模板 JSON（TS 内容）+ 样例 Fields，复用渲染引擎返回第一页 PNG base64。
    /// 不落库、不触发打印，仅用于设计器所见即所得。
    /// </summary>
    [Route("api/templates/preview")]
    [HttpPost]
    public async Task<IActionResult> Preview([FromBody] TemplatePreviewDto dto, CancellationToken ct = default)
    {
        var engine = HttpContext.RequestServices.GetRequiredService<IPrintEngine>();
        var templateJson = dto.Template;
        if (string.IsNullOrWhiteSpace(templateJson))
            return BadRequest(new { error = "模板内容不能为空。" });

        List<Dictionary<string, string>> fields;
        try
        {
            fields = string.IsNullOrWhiteSpace(dto.Fields)
                ? new List<Dictionary<string, string>>()
                : JsonSerializer.Deserialize<List<Dictionary<string, string>>>(dto.Fields)
                  ?? new List<Dictionary<string, string>>();
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "样例 Fields 解析失败：" + ex.Message });
        }

        var message = new PrintMessage
        {
            TemplateKind = PrintMessage.TemplateKindTemplateContent, // TS：直接传内容
            TemplateRef = templateJson,
            Fields = fields,
        };

        try
        {
            var base64 = await engine.RenderToPngBase64Async(message, ct);
            return Ok(new { contentType = "image/png", base64 });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "预览渲染失败：" + ex.Message });
        }
    }
}

/// <summary>模板预览请求体。</summary>
public class TemplatePreviewDto
{
    /// <summary>模板 JSON 内容（TS 模式）。</summary>
    public string? Template { get; set; }
    /// <summary>样例 Fields JSON（List&lt;Dictionary&lt;string,string&gt;&gt; 的文本）。</summary>
    public string? Fields { get; set; }
}
