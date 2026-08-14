using System.Collections.Generic;

namespace Wes.Print.Core.Print;

/// <summary>
/// 本地打印机提供程序抽象（枚举/默认打印机）。
/// </summary>
public interface IPrinterProvider
{
    string DefaultPrinterName { get; }
    IReadOnlyList<string> GetPrinters();
    bool SetDefaultPrinter(string name);
}
