using Wes.Print.Core.Storage.Entities;

namespace Wes.Print.Core.Storage;

/// <summary>
/// 本地存储服务：管理 MQ 配置与打印记录。
/// </summary>
public interface IStorage
{
    // ---- MQ 配置 ----
    Task<MqConfig?> GetMqConfigAsync(string key = "default", CancellationToken ct = default);
    Task<MqConfig> SaveMqConfigAsync(MqConfig config, CancellationToken ct = default);

    // ---- 打印记录 ----
    Task<PrintRecord> AddPrintRecordAsync(PrintRecord record, CancellationToken ct = default);
    Task<PrintRecord?> GetPrintRecordAsync(long id, CancellationToken ct = default);
    Task UpdatePrintRecordAsync(PrintRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<PrintRecord>> QueryPrintRecordsAsync(
        string? channel = null,
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default);
    Task<long> CountPrintRecordsAsync(string? channel = null, string? status = null, CancellationToken ct = default);

    /// <summary>清理早于 cutoff 的打印记录，返回删除条数。</summary>
    Task<long> PurgeOldPrintRecordsAsync(DateTime cutoff, CancellationToken ct = default);

    // ---- 通用配置 ----
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    Task SetSettingAsync(string key, string? value, CancellationToken ct = default);
}
