using CommunityToolkit.Mvvm.ComponentModel;
using PcMonitor.Controls;
using PcMonitor.Models;
using PcMonitor.Services;

namespace PcMonitor.ViewModels;

public partial class SeriesOption : ObservableObject
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string BrushKey { get; init; }
    public string Unit { get; init; } = "%";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _currentText = "—";
}

public partial class RangeOption : ObservableObject
{
    public required int Seconds { get; init; }
    public required string Title { get; init; }
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
    [ObservableProperty] private IReadOnlyList<string> _xLabels = new[] { "-60 с", "-45 с", "-30 с", "-15 с", "сейчас" };

    public ChartsViewModel(MonitoringService monitor)
    {
        _monitor = monitor;
        Options = new[]
        {
            new SeriesOption { Key = MetricKeys.Cpu, Name = "CPU", BrushKey = "CpuBrush", IsSelected = true },
            new SeriesOption { Key = MetricKeys.Ram, Name = "RAM", BrushKey = "RamBrush", IsSelected = true },
            new SeriesOption { Key = MetricKeys.Gpu, Name = "GPU", BrushKey = "GpuBrush", IsSelected = true },
            new SeriesOption { Key = MetricKeys.CpuTemp, Name = "Темп. CPU", BrushKey = "TempBrush", Unit = "°C" },
            new SeriesOption { Key = MetricKeys.GpuTemp, Name = "Темп. GPU", BrushKey = "DiskBrush", Unit = "°C" },
        };
        Ranges = new[]
        {
            new RangeOption { Seconds = 60, Title = "60 секунд", IsSelected = true },
            new RangeOption { Seconds = 120, Title = "120 секунд" },
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
        Rebuild();
    }

    private void Rebuild()
    {
        int seconds = _range.Seconds;
        int points = Math.Max(2, seconds / Math.Max(1, _monitor.IntervalSeconds));
        MaxPoints = points;
        XLabels = new[] { $"-{seconds} с", $"-{seconds * 3 / 4} с", $"-{seconds / 2} с", $"-{seconds / 4} с", "сейчас" };

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
        Options.First(o => o.Key == key).CurrentText = v is double d ? $"{d:0} {unit}" : "Недоступно";
}
