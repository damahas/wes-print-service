using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wes.Print.Core.Messaging;

namespace Wes.Print.Core.Print;

/// <summary>
/// 打印引擎抽象。FastReport.OpenSource 实现（加载 .frx + 变量 + Print），参考 kp-print。
/// </summary>
public interface IPrintEngine
{
    /// <summary>渲染并打印一条消息。printerName 来自配置（空=系统默认打印机）。</summary>
    Task PrintAsync(PrintMessage message, string? printerName = null, CancellationToken ct = default);

    /// <summary>仅渲染不打印，返回第一页 PNG 的 base64（前端 &lt;img&gt; 展示），所见即所得，net10 下稳定。</summary>
    Task<string> RenderToPngBase64Async(PrintMessage message, CancellationToken ct = default);
}
