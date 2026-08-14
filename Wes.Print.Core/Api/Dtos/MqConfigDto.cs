namespace Wes.Print.Core.Api.Dtos;

public class MqConfigDto
{
    public string Key { get; set; } = "default";
    public string Type { get; set; } = "RabbitMQ";     // RabbitMQ / Kafka
    public bool Enabled { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5672;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? Queue { get; set; }
    public string? GroupId { get; set; }
    public string? BootstrapServers { get; set; }
    public bool AutoAck { get; set; } = true;
    public string? PrinterName { get; set; }
}
