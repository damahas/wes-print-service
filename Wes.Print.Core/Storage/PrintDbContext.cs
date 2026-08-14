using Microsoft.EntityFrameworkCore;
using Wes.Print.Core.Storage.Entities;

namespace Wes.Print.Core.Storage;

public class PrintDbContext : DbContext
{
    public PrintDbContext(DbContextOptions<PrintDbContext> options) : base(options)
    {
    }

    public DbSet<MqConfig> MqConfigs => Set<MqConfig>();
    public DbSet<PrintRecord> PrintRecords => Set<PrintRecord>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MqConfig>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Key);
        });

        modelBuilder.Entity<PrintRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Channel);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Key);
        });
    }
}
