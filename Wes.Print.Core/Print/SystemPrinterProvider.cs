using System.Collections.Generic;
using System.Drawing.Printing;

namespace Wes.Print.Core.Print;

/// <summary>
/// 基于 System.Drawing.Printing 的本地打印机提供程序（占位，后续完善）。
/// 参考 kp-print 的 Lib/LocalPrinters.cs。
/// </summary>
public class SystemPrinterProvider : IPrinterProvider
{
    public string DefaultPrinterName => new PrintDocument().PrinterSettings.PrinterName;

    public IReadOnlyList<string> GetPrinters()
    {
        var list = new List<string>();
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            list.Add(name);
        }
        return list;
    }

    public bool SetDefaultPrinter(string name)
    {
        // TODO: 调用 winspool.drv SetDefaultPrinter（参考 kp-print）
        return false;
    }
}
