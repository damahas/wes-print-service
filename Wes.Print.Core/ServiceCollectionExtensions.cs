using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wes.Print.Core.Print;
using Wes.Print.Core.Storage;

namespace Wes.Print.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Wes.Print.Core 全部服务。
    /// </summary>
    /// <param name="sqlitePath">SQLite 数据库文件路径</param>
    public static IServiceCollection AddPrintCore(this IServiceCollection services, string sqlitePath)
    {
        services.AddDbContext<PrintDbContext>(opt =>
            opt.UseSqlite($"Data Source={sqlitePath}"));

        services.AddScoped<IStorage, Wes.Print.Core.Storage.Storage>();

        // 打印核心（引擎/打印机提供程序，当前为占位实现）
        services.AddSingleton<IPrinterProvider, SystemPrinterProvider>();
        services.AddScoped<IPrintEngine, SkiaPrintEngine>();

        // 后台打印队列（单例）：API 与 MQ 都先落库+入队，由队列统一串行执行打印
        services.AddSingleton<PrintQueue>();

        // 打印任务执行器（MQ 消费 + 对外 API 共用）
        services.AddScoped<PrintJobExecutor>();

        // MQ 消费者（按类型由 ConsumerFactory 解析）
        services.AddTransient<Messaging.RabbitMq.RabbitMqConsumer>();
        services.AddTransient<Messaging.Kafka.KafkaConsumer>();

        // MQ 连接管理器（单例，统一状态/连接/重连）
        services.AddSingleton<Messaging.MqConnectionManager>();

        return services;
    }
}
