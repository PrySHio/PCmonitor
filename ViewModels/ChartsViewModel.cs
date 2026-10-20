using CommunityToolkit.Mvvm.ComponentModel;
using PcMonitor.Controls;
using PcMonitor.Models;
using PcMonitor.Services;

namespace PcMonitor.ViewModels;

public partial class SeriesOption : ObservableObject
{
    public required string Key { get; init; }
    /// <summary>Ключ названия в словаре локализации.</summary>
    public required string NameKey { get; init; }
    public string Name => Loc.T(NameKey);
    public void Refresh() => OnPropertyChanged(nameof(Name));
    public required string BrushKey { get; init; }
    public string Unit { get; init; } = "%";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _currentText = "—";
}

public partial class RangeOption : ObservableObject
{
    public required int Seconds { get; init; }
    public string Title => Loc.F("Range_Seconds", Seconds);
    public void Refresh() => OnPropertyChanged(nameof(Title));
    [ObservableProperty] private bool _isSelected;
}

public partial class ChartsViewModel : ObservableObject
{
    private readonly MonitoringService _monitor;
    private RangeOption _range = null!; // выбор запоминаем по событию: при клике на RadioButton на миг «выбраны» оба пункта

    public IReadOnlyList<SeriesOption> Options { get; }
    public IReadOnlyList<RangeOption> Ranges { get; }

    [ObservableProperty] private IReadOnlyList<ChartSeries> _series = Array.Empty<ChartSeries>();
    [ObservableProperty] private int _maxPoints = 60;
    [ObservableProperty] private IReadOnlyList<string> _xLabels = Array.Empty<string>();

    public ChartsViewModel(MonitoringService monitor)
    {
        _monitor = monitor;
        Options = new[]
        {
            new SeriesOption { Key = MetricKeys.Cpu, NameKey = "Metric_Cpu", BrushKey = "CpuBrush", IsSelected = true },
            new SeriesOption { Key = MetricKeys.Ram, NameKey = "Metric_Ram", BrushKey = "RamBrush", IsSelected = true },
            new SeriesOption { Key = MetricKeys.Gpu, NameKey = "Metric_Gpu", BrushKey = "GpuBrush", IsSelected = true },
            new SeriesOption { Key = MetricKeys.CpuTemp, NameKey = "Metric_CpuTemp", BrushKey = "TempBrush", Unit = "°C" },
            new SeriesOption { Key = MetricKeys.GpuTemp, NameKey = "Metric_GpuTemp", BrushKey = "DiskBrush", Unit = "°C" },
        };
        Ranges = new[]
        {
            new RangeOption { Seconds = 60, IsSelected = true },
            new RangeOption { Seconds = 120 },
        };

        foreach (var o in Options) o.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SeriesOption.IsSelected)) Rebuild(); };
        _range = Ranges.First(r => r.IsSelected);
        foreach (var r in Ranges)
            r.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(RangeOption.IsSelected) || !((RangeOption)s!).IsSelected) return;
                _range = (RangeOption)s;
                Rebuild();
            };

        monitor.Updated += _ => Rebuild();
        Loc.LanguageChanged += () =>
        {
            foreach (var o in Options) o.Refresh();
            foreach (var r in Ranges) r.Refresh();
            Rebuild();
        };
        Rebuild();
    }

    private void Rebuild()
    {
        int seconds = _range.Seconds;
        int points = Math.Max(2, seconds / Math.Max(1, _monitor.IntervalSeconds));
        MaxPoints = points;
        string sec = Loc.T("Unit_Sec");
        XLabels = new[] { $"-{seconds} {sec}", $"-{seconds * 3 / 4} {sec}", $"-{seconds / 2} {sec}", $"-{seconds / 4} {sec}", Loc.T("Chart_Now") };

        Series = Options.Where(o => o.IsSelected).Select(o => new ChartSeries
        {
            Name = o.Name, BrushKey = o.BrushKey, Unit = o.Unit, Values = _monitor.GetSeries(o.Key, points),
        }).ToList();

        var s = _monitor.Latest;
        if (s is null) return;
        Set(MetricKeys.Cpu, s.Cpu, "%");
        Set(MetricKeys.Ram, s.RamPercent, "%");
        Set(MetricKeys.Gpu, s.GpuLoad, "%");
        Set(MetricKeys.CpuTemp, s.CpuTemp, "°C");
        Set(MetricKeys.GpuTemp, s.GpuTemp, "°C");
    }

    private void Set(string key, double? v, string unit) =>
        Options.First(o => o.Key == key).CurrentText = v is double d ? $"{d:0} {unit}" : Loc.T("NA");
}
