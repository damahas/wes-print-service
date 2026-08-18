using System.Collections.Generic;
using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace Wes.Print.Core.Print;

/// <summary>
/// 基于 System.Drawing.Printing 的本地打印机提供程序。
/// 打印机名称枚举改用 Win32 EnumPrinters（PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS），
/// 不依赖调用进程所属用户的打印机配置，因此以 LocalSystem 运行的 Windows 服务
/// 也能正确枚举到安装到本机的打印机（含网络/连接打印机）。
/// 参考 kp-print 的 Lib/LocalPrinters.cs。
/// </summary>
public class SystemPrinterProvider : IPrinterProvider
{
    public string DefaultPrinterName
    {
        get
        {
            try
            {
                var sb = new System.Text.StringBuilder(256);
                if (GetDefaultPrinter(sb, out uint size) && size > 0 && sb.Length > 0)
                    return sb.ToString();
            }
            catch
            {
                // 回退到 System.Drawing
            }

            try { return new PrintDocument().PrinterSettings.PrinterName; }
            catch { return string.Empty; }
        }
    }

    public IReadOnlyList<string> GetPrinters()
    {
        try
        {
            return EnumPrinterNames();
        }
        catch
        {
            // 回退到 System.Drawing（仅当前用户配置）
            var list = new List<string>();
            try
            {
                foreach (string name in PrinterSettings.InstalledPrinters)
                    list.Add(name);
            }
            catch { /* 忽略 */ }
            return list;
        }
    }

    public bool SetDefaultPrinter(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        try { return WinSetDefaultPrinter(name); }
        catch { return false; }
    }

    #region Win32 EnumPrinters
    private const uint PRINTER_ENUM_LOCAL = 0x00000002;
    private const uint PRINTER_ENUM_CONNECTIONS = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PRINTER_INFO_1
    {
        public uint Flags;
        public string? pDescription;
        public string? pName;
        public string? pComment;
    }

    private static IReadOnlyList<string> EnumPrinterNames()
    {
        var names = new List<string>();
        uint flags = PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS;
        uint needed = 0, returned = 0;

        // 第一次调用获取所需缓冲区大小
        EnumPrinters(flags, null, 1, IntPtr.Zero, 0, ref needed, ref returned);
        if (needed == 0) return names;

        IntPtr buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumPrinters(flags, null, 1, buffer, needed, ref needed, ref returned))
                return names;

            int count = (int)returned;
            int structSize = Marshal.SizeOf<PRINTER_INFO_1>();
            long baseAddr = buffer.ToInt64();
            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<PRINTER_INFO_1>((IntPtr)(baseAddr + i * structSize));
                if (!string.IsNullOrEmpty(info.pName))
                    names.Add(info.pName!);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        // 去重并保持稳定顺序
        var seen = new HashSet<string>();
        var result = new List<string>();
        foreach (var n in names)
        {
            if (seen.Add(n)) result.Add(n);
        }
        return result;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool EnumPrinters(
        uint flags,
        string? name,
        uint level,
        IntPtr pPrinterEnum,
        uint cbBuf,
        ref uint pcbNeeded,
        ref uint pcReturned);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetDefaultPrinter(System.Text.StringBuilder? pszBuffer, out uint pcchBuffer);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool WinSetDefaultPrinter(string printerName);
    #endregion
}
