using Microsoft.EntityFrameworkCore;

namespace PcMonitor.Data;

public sealed class PcMonitorDbContext : DbContext
{
    private readonly string _connectionString;

    public PcMonitorDbContext(string connectionString) => _connectionString = connectionString;

    public DbSet<MetricRecord> MetricsHistory => Set<MetricRecord>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connectionString);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MetricRecord>(e =>
        {
            e.ToTable("MetricsHistory");
            e.HasKey(x => x.Id);
            e.Property(x => x.MetricType).IsRequired().HasMaxLength(32);
            e.Property(x => x.DeviceName).HasMaxLength(128);
            // основной сценарий — выборка одного показателя за период
            e.HasIndex(x => new { x.MetricType, x.Timestamp });
            e.HasIndex(x => x.Timestamp);
        });
    }
}
