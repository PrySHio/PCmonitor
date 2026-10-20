using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PcMonitor.Controls;
using PcMonitor.Models;
using PcMonitor.Services;

namespace PcMonitor.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly MonitoringService _monitor;
    private readonly AppSettings _settings;

    public MetricCardViewModel Cpu { get; } = new("Card_Processor");
    public MetricCardViewModel Ram { get; } = new("Card_Ram");
    public MetricCardViewModel Gpu { get; } = new("Card_Gpu");
    public MetricCardViewModel CpuTemp { get; } = new("Card_Processor");
    public MetricCardViewModel GpuTemp { get; } = new("Card_Gpu");
    public ObservableCollection<DiskItemViewModel> Disks { get; } = new();

    [ObservableProperty] private IReadOnlyList<ChartSeries> _overview = Array.Empty<ChartSeries>();
    [ObservableProperty] private string _updatedText = Loc.T("Dash_Waiting");
    [ObservableProperty] private string _alertText = "";
    [ObservableProperty] private bool _hasAlert;

    public bool IsDemo => _monitor.Provider.IsDemo;
    public string SourceName => _monitor.Provider.SourceName;

    public DashboardViewModel(MonitoringService monitor, AppSettings settings)
    {
        _monitor = monitor;
        _settings = settings;
        monitor.Updated += Update;
        if (monitor.Latest is not null) Update(monitor.Latest);
        Loc.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        foreach (var card in new[] { Cpu, Ram, Gpu, CpuTemp, GpuTemp }) card.RefreshTitle();
        OnPropertyChanged(nameof(SourceName));
        if (_monitor.Latest is { } latest) Update(latest);
        else UpdatedText = Loc.T("Dash_Waiting");
    }

    private ChartSeries Line(string key, string name, string brush, string unit = "%") =>
        new() { Name = name, BrushKey = brush, Unit = unit, Values = _monitor.GetSeries(key, 60) };

    private void Update(MetricsSnapshot s)
    {
        var alerts = new List<string>();

        var cpuLevel = Fmt.LevelFor(s.Cpu, _settings.CpuThreshold);
        Cpu.Set(s.Cpu, "%", Loc.T("Dash_CpuSubtitle"), cpuLevel, Line(MetricKeys.Cpu, "CPU", "CpuBrush"));
        if (cpuLevel == Level.Critical) alerts.Add($"CPU {s.Cpu:0} %");

        var ramLevel = Fmt.LevelFor(s.RamPercent, _settings.RamThreshold);
        Ram.Set(s.RamPercent, "%",
            Loc.F("Dash_RamSubtitle", Fmt.Gb(s.RamUsedGb), Fmt.Gb(s.RamTotalGb), Fmt.Gb(s.RamTotalGb - s.RamUsedGb)),
            ramLevel, Line(MetricKeys.Ram, "RAM", "RamBrush"));
        if (ramLevel == Level.Critical) alerts.Add($"RAM {s.RamPercent:0} %");

        if (s.GpuLoad is double gl)
        {
            var gpuLevel = Fmt.LevelFor(gl, _settings.GpuThreshold);
            var vram = s.GpuVramPercent is double vp
                ? Loc.F("Dash_Vram", Fmt.Gb(s.GpuVramUsedGb!.Value), Fmt.Gb(s.GpuVramTotalGb!.Value), vp)
                : Loc.T("Dash_GpuLoad");
            Gpu.Set(gl, "%", vram, gpuLevel, Line(MetricKeys.Gpu, "GPU", "GpuBrush"));
            if (gpuLevel == Level.Critical) alerts.Add($"GPU {gl:0} %");
        }
        else Gpu.SetUnavailable(Loc.T("Dash_GpuNone"));

        SetTemp(CpuTemp, s.CpuTemp, Loc.T("Dash_CpuTempNA"), alerts, "CPU");
        SetTemp(GpuTemp, s.GpuTemp, Loc.T("Dash_GpuTempNA"), alerts, "GPU");

        SyncDisks(s.Disks);

        Overview = new[]
        {
            Line(MetricKeys.Cpu, "CPU", "CpuBrush"),
            Line(MetricKeys.Ram, "RAM", "RamBrush"),
            Line(MetricKeys.Gpu, "GPU", "GpuBrush"),
        };

        UpdatedText = Loc.F("Dash_Updated", s.Time, _monitor.IntervalSeconds);
        HasAlert = _settings.NotificationsEnabled && alerts.Count > 0;
        AlertText = HasAlert ? Loc.T("Dash_AlertPrefix") + string.Join(", ", alerts) : "";
    }

    private void SetTemp(MetricCardViewModel vm, double? value, string reason, List<string> alerts, string name)
    {
        if (value is double t)
        {
            var level = Fmt.LevelFor(t, _settings.TempThreshold);
            vm.Set(t, "°C", "", level);
            if (level == Level.Critical) alerts.Add($"{name} {t:0} °C");
        }
        else vm.SetUnavailable(reason);
    }

    private void SyncDisks(IReadOnlyList<DiskInfo> disks)
    {
        while (Disks.Count > disks.Count) Disks.RemoveAt(Disks.Count - 1);
        while (Disks.Count < disks.Count) Disks.Add(new DiskItemViewModel());
        for (int i = 0; i < disks.Count; i++) Disks[i].Update(disks[i]);
    }
}
