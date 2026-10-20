using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PcMonitor.Data;

/// <summary>
/// История в локальной SQLite-базе (%AppData%\PcMonitor\history.db). Для каждой операции создаётся
/// короткоживущий контекст, поэтому репозиторий безопасно использовать из нескольких потоков.
/// База создаётся при первом обращении; ошибки доступа пробрасываются вызывающему коду.
/// </summary>
public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public SqliteHistoryRepository(string? path = null)
    {
        Location = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PcMonitor", "history.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Location }.ToString();
    }

    public string Location { get; }

    private async Task<PcMonitorDbContext> OpenAsync(CancellationToken ct)
    {
        var db = new PcMonitorDbContext(_connectionString);
        if (_initialized) return db;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_initialized)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Location)!);
                await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
                _initialized = true;
            }
        }
        catch
        {
            db.Dispose();
            throw;
        }
        finally
        {
            _initLock.Release();
        }
        return db;
    }

    public async Task AddRangeAsync(IReadOnlyCollection<MetricRecord> records, CancellationToken ct = default)
    {
        if (records.Count == 0) return;
        using var db = await OpenAsync(ct).ConfigureAwait(false);
        db.MetricsHistory.AddRange(records);
        await db.SaveChangesAsync(ct).ConfigureAwait(false); // одна транзакция на пакет
    }

    public async Task<IReadOnlyList<HistoryBucket>> GetSeriesAsync(string metricType, DateTime fromUtc, DateTime toUtc,
                                                                   int buckets, CancellationToken ct = default)
    {
        long fromSec = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        long toSec = new DateTimeOffset(DateTime.SpecifyKind(toUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        long size = Math.Max(1, (toSec - fromSec + buckets - 1) / Math.Max(1, buckets)); // секунд в одном интервале

        using var db = await OpenAsync(ct).ConfigureAwait(false);
        var rows = await db.MetricsHistory.AsNoTracking()
            .Where(r => r.MetricType == metricType && r.Timestamp >= fromSec && r.Timestamp <= toSec)
            .GroupBy(r => (r.Timestamp - fromSec) / size)
            .Select(g => new
            {
                Index = g.Key,
                Avg = g.Average(x => x.Value),
                Min = g.Min(x => x.Value),
                Max = g.Max(x => x.Value),
                Count = g.Count(),
            })
            .OrderBy(x => x.Index)
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(r => new HistoryBucket(r.Index, r.Avg, r.Min, r.Max, r.Count)).ToList();
    }

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        using var db = await OpenAsync(ct).ConfigureAwait(false);
        return await db.MetricsHistory.LongCountAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        long cutoff = new DateTimeOffset(DateTime.SpecifyKind(cutoffUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        using var db = await OpenAsync(ct).ConfigureAwait(false);
        return await db.MetricsHistory.Where(r => r.Timestamp < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        using var db = await OpenAsync(ct).ConfigureAwait(false);
        await db.MetricsHistory.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        try { await db.Database.ExecuteSqlRawAsync("VACUUM", ct).ConfigureAwait(false); } // вернуть место на диске
        catch { /* уменьшение файла — необязательная оптимизация */ }
    }
}
