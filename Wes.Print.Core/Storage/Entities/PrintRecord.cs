namespace Wes.Print.Core.Storage.Entities;

/// <summary>
/// 打印记录（每次打印任务一条），用于审计与排障。
/// </summary>
public class PrintRecord
{
    public long Id { get; set; }

    /// <summary>打印渠道：Api / RabbitMQ / Kafka</summary>
    public string Channel { get; set; } = "Api";

    /// <summary>打印状态：Success / Failed / Pending</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>结果/错误信息描述</summary>
    public string? Message { get; set; }

    /// <summary>模板来源类型：T=服务端模板名, TS=模板内容, FL=文件下载链接</summary>
    public string? TemplateKind { get; set; }

    /// <summary>
    /// 模板引用/内容（按 TemplateKind 解释）：
    /// T → 模板文件名；TS → 模板文件内容（可能较长）；FL → 文件下载链接。
    /// 采用全量落库（方案 C），便于审计与重打。
    /// </summary>
    public string? TemplateRef { get; set; }

    /// <summary>实际使用的打印机名称</summary>
    public string? PrinterName { get; set; }

    /// <summary>来源标识（如 MQ 消息 id、请求来源）</summary>
    public string? SourceRef { get; set; }

    /// <summary>
    /// 本次打印提交的原始参数（Fields，key/value 字段列表）序列化 JSON。
    /// 用于审计与排障，可在后台"查看"中展开。
    /// </summary>
    public string? Request { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
