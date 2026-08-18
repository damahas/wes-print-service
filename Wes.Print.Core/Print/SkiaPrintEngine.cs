using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using Wes.Print.Core.Messaging;
using Wes.Print.Core.Print.Template;

namespace Wes.Print.Core.Print;

/// <summary>
/// 基于 SkiaSharp（MIT，纯 .NET，无 GDI+ 依赖）的免费打印引擎。
/// 原为 FastReport.OpenSource（net10 下 GDI+ 渲染失效导致空白），已迁移至 Skia 位图渲染。
/// 模板为 JSON 文件（业务人员可改），支持小标签（如 8cm×5cm）与 A4 纸。
/// 模板来源语义沿用 PrintMessage 的 T/TS/FL：
///   T  → PrintTemp 目录下的 .json 模板文件名
///   TS → 模板内容（JSON 文本）
///   FL → 文件下载链接（.json）
/// 变量 {{field}} 由消息 Fields（每行一个数据记录）替换，多行数据渲染为多页。
/// </summary>
public class SkiaPrintEngine : IPrintEngine
{
    private readonly IPrinterProvider _printerProvider;
    private static readonly string PrintTempDir = ResolvePrintTempDir();
    private static readonly string ExportDir =
        Path.Combine(Path.GetTempPath(), "WesPrintExport");

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
        return deployed;
    }

    public SkiaPrintEngine(IPrinterProvider printerProvider)
    {
        _printerProvider = printerProvider;
    }

    public Task PrintAsync(PrintMessage message, string? printerName = null, CancellationToken ct = default)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));

        var template = ResolveTemplate(message);
        var pages = RenderPages(template, message.Fields);

        var resolvedPrinter = string.IsNullOrWhiteSpace(printerName)
            ? _printerProvider.DefaultPrinterName
            : printerName;
        var settings = new PrinterSettings { PrinterName = resolvedPrinter };
        if (!settings.IsValid)
            throw new InvalidOperationException($"打印机不存在或不可用：{resolvedPrinter}");

        PrintBitmaps(resolvedPrinter, pages);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 仅渲染不打印：返回第一页 PNG 的 base64（前端 <img> 展示），所见即所得。
    /// </summary>
    public Task<string> RenderToPngBase64Async(PrintMessage message, CancellationToken ct = default)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));

        var template = ResolveTemplate(message);
        var pages = RenderPages(template, message.Fields);
        if (pages.Count == 0)
            throw new InvalidOperationException("模板渲染为空，无可见页面。");

        using var ms = new MemoryStream();
        pages[0].Bitmap.Save(ms, ImageFormat.Png);
        var base64 = Convert.ToBase64String(ms.ToArray());
        return Task.FromResult(base64);
    }

    // ---------------- 模板解析 ----------------

    private PrintTemplate ResolveTemplate(PrintMessage message)
    {
        var (json, tempFiles) = ResolveTemplateJson(message);
        if (string.IsNullOrWhiteSpace(json))
            throw new FileNotFoundException(
                $"未找到打印模板：templateKind={message.TemplateKind}, templateRef={message.TemplateRef ?? "(空)"}");

        PrintTemplate? tpl = null;
        try
        {
            tpl = JsonSerializer.Deserialize<PrintTemplate>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("模板 JSON 解析失败：" + ex.Message, ex);
        }
        finally
        {
            foreach (var f in tempFiles)
                try { File.Delete(f); } catch { /* 忽略 */ }
        }

        if (tpl is null)
            throw new InvalidOperationException("模板内容为空或格式不正确。");
        return tpl;
    }

    private (string? json, List<string> tempFiles) ResolveTemplateJson(PrintMessage message)
    {
        var tempFiles = new List<string>();
        var kind = string.IsNullOrWhiteSpace(message.TemplateKind)
            ? PrintMessage.TemplateKindTemplate
            : message.TemplateKind.Trim().ToUpperInvariant();
        var refValue = message.TemplateRef;

        switch (kind)
        {
            case PrintMessage.TemplateKindTemplate: // T：服务端模板名
            {
                if (string.IsNullOrWhiteSpace(refValue)) return (null, tempFiles);
                var candidate = Path.Combine(PrintTempDir, refValue);
                if (!candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    candidate += ".json";
                return (File.Exists(candidate) ? File.ReadAllText(candidate) : null, tempFiles);
            }
            case PrintMessage.TemplateKindTemplateContent: // TS：模板内容（JSON 文本）
            {
                return (refValue, tempFiles);
            }
            case PrintMessage.TemplateKindFile: // FL：文件下载链接
            {
                if (string.IsNullOrWhiteSpace(refValue)) return (null, tempFiles);
                var ext = Path.GetExtension(refValue);
                var tmp = Path.Combine(Path.GetTempPath(), $"WesPrintTpl_{Guid.NewGuid():N}{ext}");
                DownloadFile(refValue, tmp);
                tempFiles.Add(tmp);
                if (string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase))
                    return (File.ReadAllText(tmp), tempFiles);
                throw new NotSupportedException(
                    $"暂不支持打印该类型文件：{ext}（仅支持 .json 模板文件）。");
            }
            default:
                return (null, tempFiles);
        }
    }

    private static void DownloadFile(string url, string destPath)
    {
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var bytes = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
        File.WriteAllBytes(destPath, bytes);
    }

    // ---------------- 渲染 ----------------

    /// <summary>将模板 + 数据渲染为每页一张位图（Fields 每行一页）。</summary>
    private List<RenderedPage> RenderPages(PrintTemplate template, List<Dictionary<string, string>> fields)
    {
        double dpi = template.Page.Dpi <= 0 ? 300 : template.Page.Dpi;
        GlobalDpi = (float)dpi; // 同步字体像素换算用的 DPI
        double mmPerUnit = UnitToMm(template.Page.Unit);
        // 像素尺寸 = (尺寸mm / 25.4) * dpi
        int pxW = (int)Math.Round(template.Page.Width * mmPerUnit / 25.4 * dpi);
        int pxH = (int)Math.Round(template.Page.Height * mmPerUnit / 25.4 * dpi);

        var rows = fields is { Count: > 0 } ? fields : new List<Dictionary<string, string>> { new() };

        var result = new List<RenderedPage>();
        foreach (var row in rows)
        {
            using var bitmap = new SKBitmap(pxW, pxH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(ParseColor(template.Page.Background, SKColors.White));
                foreach (var item in template.Items)
                {
                    double scale = mmPerUnit / 25.4 * dpi; // 单位→像素
                    float x = (float)(item.X * scale);
                    float y = (float)(item.Y * scale);
                    float w = (float)(item.W * scale);
                    float h = (float)(item.H * scale);

                    switch (item)
                    {
                        case TextItem ti:
                            DrawText(canvas, ti, row, x, y, w, h);
                            break;
                        case BarcodeItem bi:
                            BarcodeRenderer.Draw(canvas,
                                ReplaceVars(bi.Value, row), bi.Symbology,
                                new SKRect(x, y, x + w, y + h),
                                ParseColor(bi.Foreground, SKColors.Black),
                                ParseColor(bi.Background, SKColors.White));
                            break;
                        case LineItem li:
                            DrawLine(canvas, li, scale);
                            break;
                        case ImageItem ii:
                            DrawImage(canvas, ii, row, x, y, w, h);
                            break;
                    }
                }
            }

            // SKBitmap → System.Drawing.Bitmap（仅做位图搬运，无 GDI+ 报表渲染）
            var bmp = ToDrawingBitmap(bitmap);
            result.Add(new RenderedPage(bmp));
        }
        return result;
    }


    private static void DrawText(SKCanvas canvas, TextItem ti, Dictionary<string, string> row, float x, float y, float w, float h)
    {
        var text = ReplaceVars(ti.Text, row);
        // 字体大小按 mm 处理，与页面单位一致，换算为像素
        float fontSizePx = (float)(ti.FontSize / 25.4 * GlobalDpi);
        using var paint = new SKPaint
        {
            Color = ParseColor(ti.Color, SKColors.Black),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            TextSize = fontSizePx,
            Typeface = ResolveTypeface(ti.FontFamily, ti.Bold),
            TextAlign = ti.Align switch
            {
                "center" => SKTextAlign.Center,
                "right" => SKTextAlign.Right,
                _ => SKTextAlign.Left,
            },
        };

        var lines = ti.Wrap ? WrapText(text, paint, w) : new List<string> { text };
        float lineH = fontSizePx * 1.2f;
        float totalH = lineH * lines.Count;
        float startY = y;
        if (ti.VAlign == "middle") startY = y + (h - totalH) / 2;
        else if (ti.VAlign == "bottom") startY = y + (h - totalH);

        float baseline = startY + fontSizePx; // 近似基线
        foreach (var line in lines)
        {
            float drawX = x;
            if (ti.Align == "center") drawX = x + w / 2;
            else if (ti.Align == "right") drawX = x + w;
            canvas.DrawText(line, drawX, baseline, paint);
            baseline += lineH;
        }
    }

    private static SKTypeface ResolveTypeface(string? requestedFamily, bool bold)
    {
        var style = bold ? SKFontStyle.Bold : SKFontStyle.Normal;
        var families = new List<string>();
        if (!string.IsNullOrWhiteSpace(requestedFamily))
            families.Add(requestedFamily);
        families.AddRange(new[] { "Microsoft YaHei", "SimHei", "SimSun", "DengXian", "Arial" });

        foreach (var family in families)
        {
            var tf = SKTypeface.FromFamilyName(family, style);
            if (tf != null && !tf.FamilyName.Equals("Default", StringComparison.OrdinalIgnoreCase))
                return tf;
            tf?.Dispose();
        }
        return SKTypeface.FromFamilyName(null, style);
    }

    private static List<string> WrapText(string text, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;
        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ');
            var current = "";
            foreach (var word in words)
            {
                var test = current.Length == 0 ? word : current + " " + word;
                if (paint.MeasureText(test) > maxWidth && current.Length > 0)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = test;
                }
            }
            if (current.Length > 0) lines.Add(current);
        }
        return lines;
    }

    private static void DrawLine(SKCanvas canvas, LineItem li, double scale)
    {
        float x1 = (float)(li.X * scale);
        float y1 = (float)(li.Y * scale);
        float x2 = (float)((li.X2 ?? (li.X + li.W)) * scale);
        float y2 = (float)((li.Y2 ?? (li.Y + li.H)) * scale);
        float width = (float)(li.Width * scale);
        using var paint = new SKPaint
        {
            Color = ParseColor(li.Color, SKColors.Black),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            IsAntialias = true,
        };
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    private static void DrawImage(SKCanvas canvas, ImageItem ii, Dictionary<string, string> row, float x, float y, float w, float h)
    {
        var src = ReplaceVars(ii.Src, row);
        if (string.IsNullOrWhiteSpace(src)) return;
        byte[]? data = null;
        try
        {
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = src.IndexOf(',');
                if (comma > 0)
                    data = Convert.FromBase64String(src[(comma + 1)..]);
            }
            else if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                data = client.GetByteArrayAsync(src).GetAwaiter().GetResult();
            }
            else
            {
                data = File.Exists(src) ? File.ReadAllBytes(src) : null;
            }
        }
        catch { data = null; }

        if (data is null) return;
        using var stream = new MemoryStream(data);
        using var skBitmap = SKBitmap.Decode(stream);
        if (skBitmap is null) return;
        canvas.DrawBitmap(skBitmap, new SKRect(x, y, x + w, y + h));
    }

    // ---------------- 辅助 ----------------

    private static double UnitToMm(string unit)
    {
        return unit?.Trim().ToLowerInvariant() switch
        {
            "cm" => 10,
            "px" => 25.4 / 96.0, // 假设 96dpi 屏幕像素
            "in" => 25.4,
            _ => 1, // mm
        };
    }

    // 全局 DPI（用于字体像素换算），与渲染一致
    private static float GlobalDpi = 300;

    private static SKColor ParseColor(string? hex, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            var c = hex.Trim();
            if (c[0] != '#') c = "#" + c;
            return SKColor.Parse(c);
        }
        catch { return fallback; }
    }

    private static string ReplaceVars(string template, Dictionary<string, string> row)
    {
        if (string.IsNullOrEmpty(template)) return template ?? "";
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{\{\s*(\w+)\s*\}\}", m =>
        {
            var key = m.Groups[1].Value;
            return row.TryGetValue(key, out var v) ? (v ?? "") : "";
        });
    }

    private static Bitmap ToDrawingBitmap(SKBitmap skBitmap)
    {
        using var ms = new MemoryStream();
        skBitmap.Encode(ms, SKEncodedImageFormat.Png, 100);
        ms.Position = 0;
        return new Bitmap(ms);
    }

    private static void PrintBitmaps(string printerName, List<RenderedPage> pages)
    {
        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = printerName;
        doc.PrinterSettings.Copies = 1;
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

        int index = 0;
        doc.PrintPage += (sender, e) =>
        {
            var img = pages[index].Bitmap;
            // 以物理尺寸铺满页面可打印区域（保持图片分辨率不被缩放失真）
            var bounds = e.PageBounds;
            float scale = Math.Min((float)bounds.Width / img.Width, (float)bounds.Height / img.Height);
            int w = (int)(img.Width * scale);
            int h = (int)(img.Height * scale);
            int x = (bounds.Width - w) / 2;
            int y = (bounds.Height - h) / 2;
            e.Graphics.DrawImage(img, x, y, w, h);
            e.HasMorePages = ++index < pages.Count;
        };
        doc.Print();
    }

    private class RenderedPage : IDisposable
    {
        public Bitmap Bitmap { get; }
        public RenderedPage(Bitmap bmp) => Bitmap = bmp;
        public void Dispose() => Bitmap.Dispose();
    }
}
