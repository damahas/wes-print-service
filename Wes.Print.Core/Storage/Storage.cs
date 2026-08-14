using Microsoft.EntityFrameworkCore;
using Wes.Print.Core.Storage.Entities;

namespace Wes.Print.Core.Storage;

public class Storage : IStorage
{
    private readonly PrintDbContext _db;

    public Storage(PrintDbContext db)
    {
        _db = db;
    }

    // ---- MQ 配置 ----
    public async Task<MqConfig?> GetMqConfigAsync(string key = "default", CancellationToken ct = default)
    {
        return await _db.MqConfigs.FirstOrDefaultAsync(x => x.Key == key, ct);
    }

    public async Task<MqConfig> SaveMqConfigAsync(MqConfig config, CancellationToken ct = default)
    {
        config.UpdatedAt = DateTime.UtcNow;
        var existing = await _db.MqConfigs.FirstOrDefaultAsync(x => x.Key == config.Key, ct);
        if (existing is null)
        {
            _db.MqConfigs.Add(config);
        }
        else
        {
            existing.Type = config.Type;
            existing.Enabled = config.Enabled;
            existing.Host = config.Host;
            existing.Port = config.Port;
            existing.UserName = config.UserName;
            existing.Password = config.Password;
            existing.Queue = config.Queue;
            existing.GroupId = config.GroupId;
            existing.BootstrapServers = config.BootstrapServers;
            existing.AutoAck = config.AutoAck;
            existing.PrinterName = config.PrinterName;
            existing.UpdatedAt = config.UpdatedAt;
        }
        await _db.SaveChangesAsync(ct);
        return existing ?? config;
    }

    // ---- 打印记录 ----
    public async Task<PrintRecord> AddPrintRecordAsync(PrintRecord record, CancellationToken ct = default)
    {
        _db.PrintRecords.Add(record);
        await _db.SaveChangesAsync(ct);
        return record;
    }

    public async Task<PrintRecord?> GetPrintRecordAsync(long id, CancellationToken ct = default)
    {
        return await _db.PrintRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task UpdatePrintRecordAsync(PrintRecord record, CancellationToken ct = default)
    {
        _db.PrintRecords.Update(record);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PrintRecord>> QueryPrintRecordsAsync(
        string? channel = null, string? status = null, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var q = _db.PrintRecords.AsNoTracking().OrderByDescending(x => x.CreatedAt);
        if (!string.IsNullOrEmpty(channel)) q = (IOrderedQueryable<PrintRecord>)q.Where(x => ChannelAliases(channel).Contains(x.Channel));
        if (!string.IsNullOrEmpty(status)) q = (IOrderedQueryable<PrintRecord>)q.Where(x => x.Status == status);
        var list = await q.Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return list;
    }

    public async Task<long> CountPrintRecordsAsync(string? channel = null, string? status = null, CancellationToken ct = default)
    {
        var q = _db.PrintRecords.AsQueryable();
        if (!string.IsNullOrEmpty(channel)) q = q.Where(x => ChannelAliases(channel).Contains(x.Channel));
        if (!string.IsNullOrEmpty(status)) q = q.Where(x => x.Status == status);
        return await q.LongCountAsync(ct);
    }

    public async Task<long> PurgeOldPrintRecordsAsync(DateTime cutoff, CancellationToken ct = default)
    {
        // 单条参数化 DELETE：SQLite 走 CreatedAt 索引，O(匹配行) 直接删除。
        // 优于先拉主键再 EF 逐条删除（避免大批量时内存/事务膨胀）。
        var rows = await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM PrintRecords WHERE CreatedAt < @cutoff",
            new Microsoft.Data.Sqlite.SqliteParameter("@cutoff", cutoff));
        return rows;
    }

    /// <summary>渠道别名集合（可翻译为 SQL IN）：RabbitMQ 与历史旧值 MQ/RabbitMq 互认，其余为单值。</summary>
    private static System.Collections.Generic.HashSet<string> ChannelAliases(string? ch)
    {
        var s = (ch ?? string.Empty).Trim();
        if (s.Equals("MQ", StringComparison.OrdinalIgnoreCase)
            || s.Equals("RabbitMq", StringComparison.OrdinalIgnoreCase)
            || s.Equals("RabbitMQ", StringComparison.OrdinalIgnoreCase))
            return new System.Collections.Generic.HashSet<string> { "MQ", "RabbitMq", "RabbitMQ" };
        return new System.Collections.Generic.HashSet<string> { s };
    }


    // ---- 通用配置 ----
    public async Task<string?> GetSettingAsync(string key, CancellationToken ct = default)
    {
        var s = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        return s?.Value;
    }

    public async Task SetSettingAsync(string key, string? value, CancellationToken ct = default)
    {
        var s = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (s is null)
        {
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
        }
        else
        {
            s.Value = value;
            s.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }
}
