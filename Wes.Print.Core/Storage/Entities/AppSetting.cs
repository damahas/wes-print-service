namespace Wes.Print.Core.Storage.Entities;

/// <summary>
/// 通用键值配置。
/// </summary>
public class AppSetting
{
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
