using CommunityToolkit.Mvvm.ComponentModel;
using PcMonitor.Controls;
using PcMonitor.Models;

namespace PcMonitor.ViewModels;

public partial class MetricCardViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _valueText = "Недоступно";
    [ObservableProperty] private string _unit = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _statusText = "Нет данных";
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private Level _level;
    [ObservableProperty] private double _barValue;
    [ObservableProperty] private IReadOnlyList<ChartSeries> _series = Array.Empty<ChartSeries>();

    public MetricCardViewModel(string title) => _title = title;

    public void Set(double value, string unit, string subtitle, Level level, ChartSeries? series = null)
    {
        IsAvailable = true;
        ValueText = value.ToString("0");
        Unit = unit;
        Subtitle = subtitle;
        Level = level;
        StatusText = Fmt.StatusText(level);
        BarValue = value;
        if (series is not null) Series = new[] { series };
    }

    /// <summary>Показатель недоступен: вместо числа — понятное обозначение (ТЗ 4.1.5, 4.6.4).</summary>
    public void SetUnavailable(string reason)
    {
        IsAvailable = false;
        ValueText = "Недоступно";
        Unit = "";
        Subtitle = reason;
        Level = Level.Normal;
        StatusText = "Нет данных";
        BarValue = 0;
        Series = Array.Empty<ChartSeries>();
    }
}

public partial class DiskItemViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _usedText = "";
    [ObservableProperty] private string _freeText = "";
    [ObservableProperty] private double _percent;
    [ObservableProperty] private Level _level;

    public void Update(DiskInfo d)
    {
        Name = d.Name;
        Label = d.Label;
        UsedText = $"{Fmt.Gb(d.UsedGb)} из {Fmt.Gb(d.TotalGb)}";
        FreeText = $"свободно {Fmt.Gb(d.FreeGb)}";
        Percent = d.Percent;
        Level = d.Percent >= 90 ? Level.Critical : d.Percent >= 75 ? Level.Warning : Level.Normal;
    }
}
