using System.Collections.Generic;

namespace Wes.Print.Core.Api.Dtos;

/// <summary>
/// 对外提交打印任务请求体。字段与 MQ 消息结构一致：
/// templateKind / templateRef / fields（见 Wes.Print.Core.Messaging.PrintMessage）。
/// </summary>
public class SubmitPrintJobDto
{
    /// <summary>模板来源类型：T=服务端模板名, TS=模板内容, FL=文件下载链接（默认 T）</summary>
    public string TemplateKind { get; set; } = "T";

    /// <summary>
    /// 与 templateKind 对应的模板引用/内容：
    /// T  → 模板文件名（服务端 PrintTemp 目录下的 .json，可不含扩展名）
    /// TS → 模板文件内容（JSON 文本）
    /// FL → 文件下载链接（http/https，当前支持 .json）
    /// </summary>
    public string? TemplateRef { get; set; }

    /// <summary>打印数据源（key=value 字段字典）。多行可放多条。</summary>
    public List<Dictionary<string, string>>? Fields { get; set; }

    /// <summary>来源标识（调用方业务单号等，用于记录 SourceRef）</summary>
    public string? SourceRef { get; set; }
}

/// <summary>
/// 对外提交打印任务的响应。
/// </summary>
public class SubmitPrintJobResultDto
{
    /// <summary>落库的打印记录 Id</summary>
    public long RecordId { get; set; }

    /// <summary>打印状态：Success / Failed</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>结果/错误信息</summary>
    public string? Message { get; set; }

    /// <summary>实际使用的打印机名称</summary>
    public string? PrinterName { get; set; }
}
