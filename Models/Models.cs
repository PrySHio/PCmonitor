namespace PcMonitor.Models;

public enum Level { Normal, Warning, Critical }

public sealed record DiskInfo(string Name, string Label, double TotalGb, double UsedGb)
{
    public double FreeGb => TotalGb - UsedGb;
    public double Percent => TotalGb <= 0 ? 0 : UsedGb / TotalGb * 100;
}

/// <summary>Один замер. null означает «показатель недоступен» — вымышленные значения не подставляются (ТЗ 4.6.4).</summary>
public sealed record MetricsSnapshot(
    DateTime Time,
    double Cpu,
    double RamUsedGb,
    double RamTotalGb,
    double? GpuLoad,
    double? GpuVramUsedGb,
    double? GpuVramTotalGb,
    double? CpuTemp,
    double? GpuTemp,
    IReadOnlyList<DiskInfo> Disks)
{
    public double RamPercent => RamTotalGb <= 0 ? 0 : RamUsedGb / RamTotalGb * 100;
    public double? GpuVramPercent =>
        GpuVramTotalGb is > 0 && GpuVramUsedGb is not null ? GpuVramUsedGb / GpuVramTotalGb * 100 : null;
}

/// <summary>Статические сведения о компонентах для страницы «Система».</summary>
public sealed record ComponentInfo(string Title, string IconKey, IReadOnlyList<KeyValuePair<string, string>> Rows);

/// <summary>Ключи показателей, общие для графиков, истории и БД (MetricType).</summary>
public static class MetricKeys
{
    public const string Cpu = "CPU";
    public const string Ram = "RAM";
    public const string Gpu = "GPU";
    public const string CpuTemp = "CPU_TEMP";
    public const string GpuTemp = "GPU_TEMP";
}
