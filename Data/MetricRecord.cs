using System.ComponentModel.DataAnnotations.Schema;

namespace PcMonitor.Data;

/// <summary>Строка таблицы MetricsHistory (Приложение А ТЗ). Время хранится как Unix-секунды UTC.</summary>
public class MetricRecord
{
    public long Id { get; set; }
    public long Timestamp { get; set; }
    public string MetricType { get; set; } = "";
    public double Value { get; set; }
    public string? DeviceName { get; set; }

    [NotMapped]
    public DateTime TimeUtc => DateTimeOffset.FromUnixTimeSeconds(Timestamp).UtcDateTime;
}

/// <summary>Агрегат за интервал времени: на графике одна точка вместо тысяч записей.</summary>
public sealed record HistoryBucket(long Index, double Average, double Min, double Max, int Count);
