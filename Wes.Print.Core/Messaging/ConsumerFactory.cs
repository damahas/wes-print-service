using Microsoft.Extensions.DependencyInjection;
using Wes.Print.Core.Messaging.Kafka;
using Wes.Print.Core.Messaging.RabbitMq;

namespace Wes.Print.Core.Messaging;

/// <summary>
/// 按配置类型创建对应的 MQ 消费者，实现可扩展性。
/// 新增 MQ 实现：在对应子目录实现 IPrintMessageConsumer，并在此注册。
/// </summary>
public static class ConsumerFactory
{
    public static IPrintMessageConsumer Create(string type, IServiceProvider? sp = null)
    {
        return type.Trim().ToLowerInvariant() switch
        {
            "rabbitmq" => sp?.GetRequiredService<RabbitMqConsumer>() ?? new RabbitMqConsumer(),
            "kafka" => sp?.GetRequiredService<KafkaConsumer>() ?? new KafkaConsumer(),
            _ => throw new NotSupportedException($"不支持的 MQ 类型: {type}"),
        };
    }
}
