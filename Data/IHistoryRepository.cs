namespace PcMonitor.Data;

/// <summary>Хранилище истории. Интерфейс позволяет тестировать логику без реальной БД (ТЗ 4.8.2).</summary>
public interface IHistoryRepository
{
    string Location { get; }

    Task AddRangeAsync(IReadOnlyCollection<MetricRecord> records, CancellationToken ct = default);

    /// <summary>Записи показателя за период, сгруппированные в не более чем <paramref name="buckets"/> интервалов.</summary>
    Task<IReadOnlyList<HistoryBucket>> GetSeriesAsync(string metricType, DateTime fromUtc, DateTime toUtc,
                                                      int buckets, CancellationToken ct = default);

    Task<long> CountAsync(CancellationToken ct = default);

    /// <summary>Удаляет записи старше границы (ТЗ 4.7.4). Возвращает число удалённых строк.</summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);

    Task ClearAsync(CancellationToken ct = default);
}
