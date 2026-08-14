using System.Text.Json.Serialization;

namespace Wes.Print.Core.Print.Template;

/// <summary>
/// JSON 打印模板模型（业务人员可改，无需编译）。
/// 取代原 FastReport .frx 模板，基于 SkiaSharp 纯 .NET 渲染，无 GDI+ 依赖，net10 稳定。
/// 模板来源语义沿用 PrintMessage 的 T/TS/FL：
///   T  → PrintTemp 目录下的 .json 文件名
///   TS → 模板内容（JSON 文本）
///   FL → 文件下载链接（.json）
/// </summary>
public class PrintTemplate
{
    /// <summary>页面设置（纸张尺寸、单位）。</summary>
    [JsonPropertyName("page")]
    public PageSetting Page { get; set; } = new();

    /// <summary>元素列表（文字/条码/线条/图片）。</summary>
    [JsonPropertyName("items")]
    public List<TemplateItem> Items { get; set; } = new();
}

public class PageSetting
{
    /// <summary>纸张宽度，单位见 Unit（默认 mm）。</summary>
    [JsonPropertyName("width")]
    public double Width { get; set; } = 80;

    /// <summary>纸张高度，单位见 Unit（默认 mm）。</summary>
    [JsonPropertyName("height")]
    public double Height { get; set; } = 50;

    /// <summary>单位：mm / px / cm。标签常用 mm（如 8cm×5cm 即 80×50mm），A4 用 210×297mm。</summary>
    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "mm";

    /// <summary>渲染 DPI（默认 300，保证条码/小字打印清晰）。</summary>
    [JsonPropertyName("dpi")]
    public int Dpi { get; set; } = 300;

    /// <summary>背景色（可选，默认白）。</summary>
    [JsonPropertyName("background")]
    public string Background { get; set; } = "#FFFFFF";
}

/// <summary>模板元素基类。多态反序列化由 TemplateItemConverter 依据 type 字段完成。</summary>
[JsonConverter(typeof(TemplateItemConverter))]
public abstract class TemplateItem
{
    /// <summary>元素类型：text / barcode / line / image。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    /// <summary>左上角 X（单位与 page.unit 一致）。</summary>
    [JsonPropertyName("x")]
    public double X { get; set; }

    /// <summary>左上角 Y（单位与 page.unit 一致）。</summary>
    [JsonPropertyName("y")]
    public double Y { get; set; }

    /// <summary>宽度（单位与 page.unit 一致）。</summary>
    [JsonPropertyName("w")]
    public double W { get; set; }

    /// <summary>高度（单位与 page.unit 一致）。</summary>
    [JsonPropertyName("h")]
    public double H { get; set; }
}

public class TextItem : TemplateItem
{
    public TextItem() => Type = "text";

    /// <summary>文本内容，支持 {{field}} 变量占位符。</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>字体大小（单位与 page.unit 一致，mm 下常见 3~12）。</summary>
    [JsonPropertyName("font")]
    public double FontSize { get; set; } = 4;

    /// <summary>是否加粗。</summary>
    [JsonPropertyName("bold")]
    public bool Bold { get; set; }

    /// <summary>字体名（默认 Arial）。</summary>
    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Arial";

    /// <summary>字体颜色（默认黑）。</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#000000";

    /// <summary>对齐：left / center / right。</summary>
    [JsonPropertyName("align")]
    public string Align { get; set; } = "left";

    /// <summary>垂直对齐：top / middle / bottom。</summary>
    [JsonPropertyName("valign")]
    public string VAlign { get; set; } = "top";

    /// <summary>是否自动换行。</summary>
    [JsonPropertyName("wrap")]
    public bool Wrap { get; set; } = true;
}

public class BarcodeItem : TemplateItem
{
    public BarcodeItem() => Type = "barcode";

    /// <summary>条码值，支持 {{field}} 变量。</summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    /// <summary>条码类型：QR / CODE128。</summary>
    [JsonPropertyName("symbology")]
    public string Symbology { get; set; } = "QR";

    /// <summary>前景色（默认黑）。</summary>
    [JsonPropertyName("foreground")]
    public string Foreground { get; set; } = "#000000";

    /// <summary>背景色（默认白）。</summary>
    [JsonPropertyName("background")]
    public string Background { get; set; } = "#FFFFFF";

    /// <summary>是否在条码下方显示可读文本（默认 false）。</summary>
    [JsonPropertyName("showText")]
    public bool ShowText { get; set; }
}

public class LineItem : TemplateItem
{
    public LineItem() => Type = "line";

    /// <summary>终点 X（单位与 page.unit 一致），不填则用 x + w。</summary>
    [JsonPropertyName("x2")]
    public double? X2 { get; set; }

    /// <summary>终点 Y（单位与 page.unit 一致），不填则用 y + h。</summary>
    [JsonPropertyName("y2")]
    public double? Y2 { get; set; }

    /// <summary>线宽（单位与 page.unit 一致）。</summary>
    [JsonPropertyName("width")]
    public double Width { get; set; } = 0.3;

    /// <summary>颜色（默认黑）。</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#000000";
}

public class ImageItem : TemplateItem
{
    public ImageItem() => Type = "image";

    /// <summary>图片来源：支持 {{field}} 变量（URL 或 base64 data URI）。</summary>
    [JsonPropertyName("src")]
    public string Src { get; set; } = "";
}
