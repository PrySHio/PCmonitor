using PcMonitor.Models;

namespace PcMonitor.Services;

/// <summary>
/// Источник показателей. Сейчас реализован DemoMetricsProvider; реальный
/// (PerformanceCounter + LibreHardwareMonitor) подключается без изменений в UI.
/// </summary>
public interface IMetricsProvider
{
    string SourceName { get; }
    bool IsDemo { get; }
    MetricsSnapshot Read();
    IReadOnlyList<ComponentInfo> GetComponents();
}
