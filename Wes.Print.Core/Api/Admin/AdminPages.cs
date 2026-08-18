using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace Wes.Print.Core.Api;

/// <summary>
/// 管理后台页面。前端文件（index.html / app.css / app.js）从磁盘 wwwroot 实时读取，
/// 位于 Wes.Print.Core/Api/Admin/wwwroot/，便于改完硬刷新即可生效（无需重新编译）。
/// 编译时（csproj）不再将其作为嵌入资源打包。
/// </summary>
public static class AdminPages
{
    /// <summary>
    /// 解析 wwwroot 物理目录：
    /// 1) 开发态：从 dll 所在目录向上查找，找到包含 "Wes.Print.Core/Api/Admin/wwwroot" 的源码目录
    ///    （dll 可能被复制到主项目 bin 下，故向上遍历而非固定层级）
    /// 2) 部署态：wwwroot 复制到与 dll 同级的 wwwroot 目录
    /// </summary>
    private static string ResolveWwwRoot()
    {
        var dllDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        // 部署态：dll 同级 wwwroot（发布后位于根目录）
        var deployed = Path.Combine(dllDir, "wwwroot");
        if (Directory.Exists(deployed)) return deployed;

        // 开发态：从 dll 目录向上遍历，定位源码中的 wwwroot
        var dir = dllDir;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "Wes.Print.Core", "wwwroot");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }

        return deployed; // 兜底
    }

    public static void MapAdminPages(this WebApplication app)
    {
        var root = ResolveWwwRoot();
        var provider = new PhysicalFileProvider(root);

        // /admin/* 提供 wwwroot 下的静态文件（app.css、app.js、index.html 等）
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = provider,
            RequestPath = "/admin",
        });

        // 根路径 / 直接返回 index.html
        app.MapGet("/", () =>
        {
            var index = Path.Combine(root, "index.html");
            return File.Exists(index)
                ? Results.File(index, "text/html; charset=utf-8")
                : Results.NotFound("index.html 未找到");
        });
    }
}
