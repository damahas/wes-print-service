using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using System;
using System.Threading;
using Wes.Print.Core;
using Wes.Print.Core.Api;
using Wes.Print.Core.Storage;

var builder = WebApplication.CreateBuilder(args);

// 支持作为 Windows 服务运行
builder.Host.UseWindowsService();

// SQLite 路径（默认相对 exe 所在目录的 WesPrint.db）。
// 注意：必须用 AppContext.BaseDirectory 而非 Environment.CurrentDirectory，
// 因为作为 Windows 服务运行时 CurrentDirectory 是 C:\Windows\system32，
// 会导致数据库文件落在系统目录下。
var sqlitePath = builder.Configuration["Storage:SqlitePath"] ?? "WesPrint.db";
if (!Path.IsPathRooted(sqlitePath))
{
    sqlitePath = Path.Combine(AppContext.BaseDirectory, sqlitePath);
}

builder.Services.AddPrintCore(sqlitePath);
builder.Services.AddControllers()
    .AddApplicationPart(typeof(Wes.Print.Core.Api.Controllers.PrintServiceController).Assembly);

// 服务模式看不到控制台，将日志写入 Windows 事件日志
if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Logging.AddEventLog(settings => settings.SourceName = "Wes.PrintService");
}

// 端口 8809
var httpPort = builder.Configuration["Http:Port"] ?? "8809";
builder.WebHost.UseUrls($"http://0.0.0.0:{httpPort}");

var app = builder.Build();

// 确保数据库与表已初始化（全新项目，数据库可随时删除重建）
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
    db.Database.EnsureCreated();
}

// 映射 API（端口 8809，标准 Controller）
app.MapControllers();

// 管理后台页面（简约科技风）
app.MapAdminPages();

// 初始化 MQ 连接状态；若某通道开关启用且配置完整则自动连接（后台，不阻塞启动）
using (var scope = app.Services.CreateScope())
{
    var mq = scope.ServiceProvider.GetRequiredService<Wes.Print.Core.Messaging.MqConnectionManager>();
    mq.InitFromStorage();
    foreach (var st in await mq.GetAllStatusAsync())
    {
        if (st.State == "Idle")
        {
            _ = Task.Run(() => mq.StartAsync(st.Key));
        }
    }
}

// 打印记录保留策略：超过 record.retention-days（默认 30）天的记录自动清理（每日执行）
_ = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
    // 启动后立即执行一次，再进入每日周期
    await PurgeDueAsync(app.Services);
    while (await timer.WaitForNextTickAsync())
    {
        await PurgeDueAsync(app.Services);
    }
});

app.Run();

/// <summary>按保留天数清理过期打印记录（读取 record.retention-days，默认 30 天）。</summary>
static async Task PurgeDueAsync(IServiceProvider sp)
{
    try
    {
        using var scope = sp.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<Wes.Print.Core.Storage.IStorage>();
        var raw = await storage.GetSettingAsync("record.retention-days");
        var days = int.TryParse(raw, out var d) && d > 0 ? d : 30;
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var removed = await storage.PurgeOldPrintRecordsAsync(cutoff);
        if (removed > 0)
            Console.WriteLine($"[Retention] 已清理 {removed} 条超过 {days} 天的打印记录（cutoff={cutoff:u}）");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Retention] 清理失败：{ex.Message}");
    }
}
