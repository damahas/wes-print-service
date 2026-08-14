using SkiaSharp;
using QRCoder;

namespace Wes.Print.Core.Print;

/// <summary>
/// 条码渲染工具：用 SkiaSharp 绘制二维码(QR)与一维码(CODE128)，纯 .NET 无 GDI+ 依赖。
/// QR 模块矩阵由 QRCoder 生成，绘制由本类完成，避免任何 System.Drawing 调用。
/// </summary>
public static class BarcodeRenderer
{
    /// <summary>
    /// 在指定矩形区域内绘制条码。
    /// </summary>
    /// <param name="canvas">Skia 画布</param>
    /// <param name="value">条码内容</param>
    /// <param name="symbology">QR / CODE128</param>
    /// <param name="rect">绘制区域（像素）</param>
    /// <param name="foreground">前景色</param>
    /// <param name="background">背景色</param>
    public static void Draw(SKCanvas canvas, string value, string symbology, SKRect rect, SKColor foreground, SKColor background)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var sym = (symbology ?? "QR").Trim().ToUpperInvariant();
        switch (sym)
        {
            case "QR":
                DrawQr(canvas, value, rect, foreground, background);
                break;
            case "CODE128":
            case "CODE_128":
            case "C128":
                DrawCode128(canvas, value, rect, foreground, background);
                break;
            default:
                // 未知类型按 QR 兜底
                DrawQr(canvas, value, rect, foreground, background);
                break;
        }
    }

    private static void DrawQr(SKCanvas canvas, string value, SKRect rect, SKColor fg, SKColor bg)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.M);
        var modules = data.ModuleMatrix;
        int count = modules.Count;
        if (count == 0) return;

        // 留白边框（quiet zone）：每边 4 模块
        const int quiet = 4;
        int total = count + quiet * 2;

        canvas.DrawRect(rect, new SKPaint { Color = bg, Style = SKPaintStyle.Fill, IsAntialias = false });

        float cell = Math.Min(rect.Width, rect.Height) / total;
        float originX = rect.Left + (rect.Width - cell * total) / 2;
        float originY = rect.Top + (rect.Height - cell * total) / 2;

        using var fgPaint = new SKPaint { Color = fg, Style = SKPaintStyle.Fill, IsAntialias = false };
        for (int r = 0; r < count; r++)
        {
            var rowBits = modules[r];
            for (int c = 0; c < count; c++)
            {
                if (rowBits[c])
                {
                    canvas.DrawRect(
                        originX + (c + quiet) * cell,
                        originY + (r + quiet) * cell,
                        cell,
                        cell,
                        fgPaint);
                }
            }
        }
    }

    // ---- CODE128（B 子集）实现 ----
    // 编码表：值 0..106 对应 CODE128 字符；此处实现 Code128B（可打印 ASCII 32..127）
    private static readonly int[] Code128BValues =
    {
        212222, 222122, 222221, 121223, 121322, 131222, 122213, 122312, 132212, 221213,
        221312, 231212, 112232, 122132, 122231, 113222, 123122, 123221, 223211, 221132,
        221231, 213212, 223112, 312131, 311222, 321122, 321221, 312212, 322112, 322211,
        212123, 212321, 232121, 111323, 131123, 131321, 112313, 132113, 132311, 211313,
        231113, 231311, 112133, 112331, 132131, 113123, 113321, 133121, 313121, 211331,
        231131, 213113, 213311, 213131, 311123, 311321, 331121, 312113, 312311, 332111,
        314111, 221411, 431111, 111224, 111422, 121124, 121421, 141122, 141221, 112214,
        112412, 122114, 122411, 142112, 142211, 241211, 221114, 413111, 241112, 134111,
        111242, 121142, 121241, 114212, 124112, 124211, 411212, 421112, 421211, 212141,
        214121, 412121, 111143, 111341, 131141, 114113, 114311, 411113, 411311, 113141,
        114131, 311141, 411131, 211412, 211214, 211232, 233111, 211322, 321122 // 注意：此处到 103
    };

    private const int CODE128_START_B = 104;
    private const int CODE128_STOP = 106;

    private static void DrawCode128(SKCanvas canvas, string value, SKRect rect, SKColor fg, SKColor bg)
    {
        // 计算校验与编码条宽序列
        var bars = EncodeCode128B(value);
        if (bars == null || bars.Count == 0) return;

        canvas.DrawRect(rect, new SKPaint { Color = bg, Style = SKPaintStyle.Fill, IsAntialias = false });

        float fullWidth = rect.Width;
        float barWidth = fullWidth / bars.Sum();
        float x = rect.Left;
        using var fgPaint = new SKPaint { Color = fg, Style = SKPaintStyle.Fill, IsAntialias = false };
        bool black = true;
        foreach (var w in bars)
        {
            if (black)
                canvas.DrawRect(x, rect.Top, barWidth * w, rect.Height, fgPaint);
            x += barWidth * w;
            black = !black;
        }
    }

    /// <summary>
    /// 返回 CODE128B 的条宽（单位像素）序列：每个元素为单模块宽度的倍数。
    /// 每条形码字符由 6 个条/空模块组成（3 黑 3 白交替）。
    /// </summary>
    private static List<int> EncodeCode128B(string value)
    {
        if (string.IsNullOrEmpty(value)) return new List<int>();

        var charValues = new List<int> { CODE128_START_B };
        int checksum = CODE128_START_B;
        for (int i = 0; i < value.Length; i++)
        {
            int c = value[i];
            if (c < 32 || c > 127) c = 32; // 不可打印字符兜底为空格
            int v = c - 32; // Code128B: 字符值 = ASCII - 32
            charValues.Add(v);
            checksum += v * (i + 1);
        }
        checksum %= 103;
        charValues.Add(checksum);
        charValues.Add(CODE128_STOP);

        var bars = new List<int>();
        foreach (var cv in charValues)
        {
            int pattern = Code128BValues[cv];
            // pattern 为 6 位数字，每位是模块数（黑-白-黑-白-黑-白）
            int scale = (int)Math.Pow(10, 5);
            for (int d = 0; d < 6; d++)
            {
                int m = (pattern / (int)Math.Pow(10, 5 - d)) % 10;
                bars.Add(m);
            }
        }
        return bars;
    }
}
