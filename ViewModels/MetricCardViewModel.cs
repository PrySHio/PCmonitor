using CommunityToolkit.Mvvm.ComponentModel;
using PcMonitor.Controls;
using PcMonitor.Models;
using PcMonitor.Services;

namespace PcMonitor.ViewModels;

public partial class MetricCardViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _valueText = Loc.T("NA");
    [ObservableProperty] private string _unit = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _statusText = Loc.T("NoData");
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private Level _level;
    [ObservableProperty] private double _barValue;
    [ObservableProperty] private IReadOnlyList<ChartSeries> _series = Array.Empty<ChartSeries>();

    private readonly string _titleKey;

    /// <param name="titleKey">Ключ заголовка в словаре локализации.</param>
    public MetricCardViewModel(string titleKey)
    {
        _titleKey = titleKey;
        _title = Loc.T(titleKey);
    }

    /// <summary>Перечитывает заголовок после смены языка.</summary>
    public void RefreshTitle() => Title = Loc.T(_titleKey);

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
        ValueText = Loc.T("NA");
        Unit = "";
        Subtitle = reason;
        Level = Level.Normal;
        StatusText = Loc.T("NoData");
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
        UsedText = Loc.F("Disk_UsedOf", Fmt.Gb(d.UsedGb), Fmt.Gb(d.TotalGb));
        FreeText = Loc.F("Disk_Free", Fmt.Gb(d.FreeGb));
        Percent = d.Percent;
        Level = d.Percent >= 90 ? Level.Critical : d.Percent >= 75 ? Level.Warning : Level.Normal;
    }
}
