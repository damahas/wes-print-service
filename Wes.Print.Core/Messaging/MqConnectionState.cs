namespace Wes.Print.Core.Messaging;

/// <summary>
/// MQ 连接状态，驱动管理后台状态指示点与 API 返回。
/// </summary>
public enum MqConnectionState
{
    /// <summary>初始/未连接（未启用或连接已结束）</summary>
    Disconnected,

    /// <summary>未启用（开关关闭）</summary>
    Disabled,

    /// <summary>已启用但缺少必要配置（主机/队列为空）</summary>
    NoConfig,

    /// <summary>已启用且配置完整，但用户尚未手动连接也未自动连接</summary>
    Idle,

    /// <summary>正在建立连接</summary>
    Connecting,

    /// <summary>已连接，正在消费</summary>
    Connected,

    /// <summary>连接中断，正在重连</summary>
    Reconnecting,

    /// <summary>连接彻底失败（如校验不通过、被手动断开后无重连）</summary>
    Failed,

    /// <summary>已手动停止</summary>
    Stopped,
}
