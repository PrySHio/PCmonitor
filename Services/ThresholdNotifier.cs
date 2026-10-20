using PcMonitor.Models;

namespace PcMonitor.Services;

public sealed record ThresholdAlert(string MetricKey, string Name, double Value, double Threshold, string Unit)
{
    public string Text => Loc.F("Notify_Text", Name, Value, Unit, Threshold);
}

/// <summary>
/// Решает, о каких превышениях порогов сообщить. Не зависит от UI и времени системы (часы подменяются в тестах).
/// Уведомление по показателю не чаще, чем раз в NotifyCooldownMinutes — даже если значение «дребезжит» около порога
/// или превышение длится долго (ТЗ 4.1.11). Недоступные показатели (null) не проверяются.
/// </summary>
public sealed class ThresholdNotifier
{
    private readonly Func<DateTime> _clock;
    private readonly Dictionary<string, DateTime> _lastFired = new();

    public ThresholdNotifier(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.UtcNow);

    public IReadOnlyList<ThresholdAlert> Evaluate(MetricsSnapshot s, AppSettings settings)
    {
        var result = new List<ThresholdAlert>();
        if (!settings.NotificationsEnabled) return result;

        var now = _clock();
        var cooldown = TimeSpan.FromMinutes(Math.Max(1, settings.NotifyCooldownMinutes));

        Check(result, now, cooldown, MetricKeys.Cpu, "CPU", s.Cpu, settings.CpuThreshold, "%");
        Check(result, now, cooldown, MetricKeys.Ram, "RAM", s.RamPercent, settings.RamThreshold, "%");
        Check(result, now, cooldown, MetricKeys.Gpu, "GPU", s.GpuLoad, settings.GpuThreshold, "%");
        Check(result, now, cooldown, MetricKeys.CpuTemp, Loc.T("Alert_CpuTemp"), s.CpuTemp, settings.TempThreshold, "°C");
        Check(result, now, cooldown, MetricKeys.GpuTemp, Loc.T("Alert_GpuTemp"), s.GpuTemp, settings.TempThreshold, "°C");
        return result;
    }

    public void Reset() => _lastFired.Clear();

    private void Check(List<ThresholdAlert> result, DateTime now, TimeSpan cooldown,
                       string key, string name, double? value, double threshold, string unit)
    {
        if (value is null || value < threshold) return;
        if (_lastFired.TryGetValue(key, out var last) && now - last < cooldown) return;

        _lastFired[key] = now;
        result.Add(new ThresholdAlert(key, name, value.Value, threshold, unit));
    }
}
