namespace Wes.Print.Core.Messaging;

/// <summary>
/// 统一打印任务数据结构：对外 API 提交、MQ 消息内容均为本结构。
/// templateKind / templateRef / fields 为对外与 MQ 通用字段。
/// </summary>
public class PrintMessage
{
    /// <summary>
    /// 打印模板来源类型：
    /// T  = 服务端模板名（TemplateRef 为 PrintTemp 目录下的 .json 文件名）
    /// TS = 模板内容（TemplateRef 为 JSON 文本）
    /// FL = 文件下载链接（TemplateRef 为 HTTP(S) URL，仅支持 .json）
    /// </summary>
    public string TemplateKind { get; set; } = TemplateKindTemplate;

    /// <summary>
    /// 与 TemplateKind 对应的模板引用/内容：
    /// T  → 模板文件名（服务端 PrintTemp 目录下的 .json，可不含扩展名）
    /// TS → 模板文件内容（JSON 文本）
    /// FL → 文件下载链接（HTTP/HTTPS），引擎下载后按扩展名选择打印方式
    /// </summary>
    public string? TemplateRef { get; set; }

    /// <summary>
    /// 打印数据源（key/value 字段字典的列表，可多行）。每行数据渲染为一页，
    /// 模板中的 {{field}} 占位符由对应行的字段值替换。
    /// </summary>
    public List<Dictionary<string, string>> Fields { get; set; } = new();

    /// <summary>来源消息标识（MQ 投递标签 / 业务单号，用于记录 SourceRef）</summary>
    public string? MessageId { get; set; }

    #region 模板来源类型常量
    /// <summary>服务端模板名：TemplateRef 为 PrintTemp 目录下的 .json 文件名</summary>
    public const string TemplateKindTemplate = "T";
    /// <summary>模板内容：TemplateRef 为 .json 模板文本</summary>
    public const string TemplateKindTemplateContent = "TS";
    /// <summary>文件：TemplateRef 为 .json 文件下载链接</summary>
    public const string TemplateKindFile = "FL";
    #endregion
}

/// <summary>
/// MQ 消费者统一抽象。新增 MQ 类型只需实现本接口并在 ConsumerFactory 注册。
/// </summary>
public interface IPrintMessageConsumer : IDisposable
{
    /// <summary>启动消费，收到消息时回调 onMessage（应处理完打印逻辑）。</summary>
    void Start(ConsumerOptions options, Func<PrintMessage, Task> onMessage, CancellationToken ct = default);

    void Stop();
}

/// <summary>
/// 消费者配置（含多 MQ 类型所需字段，按需使用）。</summary>
public class ConsumerOptions
{
    public string Type { get; set; } = "RabbitMQ";   // RabbitMQ / Kafka
    public bool Enabled { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5672;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? Queue { get; set; }               // RabbitMQ 队列 / Kafka Topic
    public string? GroupId { get; set; }             // Kafka 消费组
    public string? PrinterName { get; set; }         // 目标打印机（来自 MQ 配置，前端下拉选择；空=系统默认）
    public string? BootstrapServers { get; set; }    // Kafka
    public bool AutoAck { get; set; } = true;
}
