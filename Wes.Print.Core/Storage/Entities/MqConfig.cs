namespace Wes.Print.Core.Storage.Entities;

/// <summary>
/// MQ 配置（持久化到 SQLite），支持 RabbitMQ / Kafka 等多种实现。
/// 通过 Type 字段区分具体 MQ 类型。
/// </summary>
public class MqConfig
{
    public int Id { get; set; }

    /// <summary>配置标识，默认 "default"</summary>
    public string Key { get; set; } = "default";

    /// <summary>MQ 类型：RabbitMQ / Kafka</summary>
    public string Type { get; set; } = "RabbitMQ";

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; }

    /// <summary>主机地址（RabbitMQ: Host；Kafka: 第一个 bootstrap server）</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>端口（RabbitMQ: 5672；Kafka 通常 9092）</summary>
    public int Port { get; set; } = 5672;

    /// <summary>用户名</summary>
    public string? UserName { get; set; }

    /// <summary>密码</summary>
    public string? Password { get; set; }

    /// <summary>RabbitMQ: 队列名 / Kafka: Topic</summary>
    public string? Queue { get; set; }

    /// <summary>Kafka 专用：消费组</summary>
    public string? GroupId { get; set; }

    /// <summary>Kafka 专用：bootstrap servers（逗号分隔，覆盖 Host/Port）</summary>
    public string? BootstrapServers { get; set; }

    /// <summary>是否自动确认</summary>
    public bool AutoAck { get; set; } = true;

    /// <summary>所选打印机名称（空表示使用系统默认打印机）</summary>
    public string? PrinterName { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
