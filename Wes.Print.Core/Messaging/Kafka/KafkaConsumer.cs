using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Wes.Print.Core.Messaging.Kafka;

/// <summary>
/// Kafka 消费者（预留扩展，后续实现）。
/// </summary>
public class KafkaConsumer : IPrintMessageConsumer
{
    public void Start(ConsumerOptions options, Func<PrintMessage, Task> onMessage, CancellationToken ct = default)
    {
        // TODO: 实现 Kafka 消费（Confluent.Kafka）
        throw new NotImplementedException("KafkaConsumer 待实现");
    }

    public void Stop()
    {
        // TODO
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
