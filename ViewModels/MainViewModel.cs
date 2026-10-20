using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcMonitor.Data;
using PcMonitor.Models;
using PcMonitor.Services;

namespace PcMonitor.ViewModels;

public partial class NavItem : ObservableObject
{
    private readonly string _titleKey;

    public NavItem(string titleKey, string iconKey, object page)
    {
        _titleKey = titleKey;
        Page = page;
        Icon = Application.Current.TryFindResource(iconKey) as Geometry ?? Geometry.Empty;
    }
    public string Title => Loc.T(_titleKey);
    public void Refresh() => OnPropertyChanged(nameof(Title));
    public Geometry Icon { get; }
    public object Page { get; }
    [ObservableProperty] private bool _isSelected;
}

public partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly MonitoringService _monitor;
    private readonly HistoryRecorder _recorder;
    private readonly IMetricsProvider _provider;

    public IReadOnlyList<NavItem> Navigation { get; }
    [ObservableProperty] private object? _currentPage;

    public NotificationService Notifications { get; } = new();
    public AppSettings Settings => _settings;
    public MonitoringService Monitor => _monitor;
    private readonly ThresholdNotifier _notifier = new();

    public bool IsDemo => _monitor.Provider.IsDemo;
    public string SourceName => _monitor.Provider.SourceName;

    public MainViewModel()
    {
        _settings = SettingsService.Load();
        // Реальные показатели Windows; демо-данные — запуск с ключом --demo (или не на Windows)
        bool demo = !OperatingSystem.IsWindows()
                    || Environment.GetCommandLineArgs().Contains("--demo", StringComparer.OrdinalIgnoreCase);
        IMetricsProvider provider = demo ? new DemoMetricsProvider() : new SystemMetricsProvider();
        _provider = provider;
        _monitor = new MonitoringService(provider, _settings.IntervalSeconds);

        IHistoryRepository history = new SqliteHistoryRepository();
        _recorder = new HistoryRecorder(history, _monitor, _settings);

        Navigation = new[]
        {
            new NavItem("Nav_Overview", "IconDashboard", new DashboardViewModel(_monitor, _settings)),
            new NavItem("Nav_Charts", "IconChart", new ChartsViewModel(_monitor)),
            new NavItem("Nav_History", "IconHistory", new HistoryViewModel(history, _recorder)),
            new NavItem("Nav_System", "IconCpu", new SystemViewModel(provider)),
            new NavItem("Nav_Settings", "IconSettings", new SettingsViewModel(_settings, _monitor, Notifications)),
        };

        foreach (var item in Navigation)
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(NavItem.IsSelected) && ((NavItem)s!).IsSelected)
                    CurrentPage = ((NavItem)s).Page;
            };

        Loc.LanguageChanged += () =>
        {
            foreach (var item in Navigation) item.Refresh();
            OnPropertyChanged(nameof(SourceName));
        };

        Navigation[0].IsSelected = true;
        _monitor.Updated += OnSnapshot;
        _monitor.Start();
        _recorder.Start();
    }

    public void ShowSettings()
    {
        var item = Navigation.FirstOrDefault(n => n.Page is SettingsViewModel);
        if (item is not null) item.IsSelected = true;
    }

    private void OnSnapshot(MetricsSnapshot s)
    {
        var alerts = _notifier.Evaluate(s, _settings);
        if (alerts.Count == 0) return;
        Notifications.Show(Loc.T("Notify_Title"), string.Join("\n", alerts.Select(a => a.Text)), _settings.NotificationSound);
    }

    /// <summary>Штатное завершение: досрочно записывает накопленные замеры в базу (ТЗ 4.2.3).</summary>
    public void Shutdown()
    {
        _monitor.Stop();
        _recorder.Stop();
        (_provider as IDisposable)?.Dispose(); // закрыть драйвер датчиков
    }

    /// <summary>Быстрое переключение тем из заголовка окна.</summary>
    [RelayCommand]
    private void CycleTheme()
    {
        var themes = ThemeService.Themes;
        int i = themes.ToList().FindIndex(t => t.Name == ThemeService.Current);
        var next = themes[(i + 1) % themes.Count];
        ThemeService.Apply(next.Name);
        _settings.Theme = next.Name;
        SettingsService.Save(_settings);
    }
}
