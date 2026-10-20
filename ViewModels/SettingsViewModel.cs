using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcMonitor.Services;

namespace PcMonitor.ViewModels;

public partial class ThemeOption : ObservableObject
{
    public ThemeOption(ThemeInfo info)
    {
        Name = info.Name; Title = info.Title;
        Background = info.Background; Surface = info.Surface; Accent = info.Accent;
    }
    public string Name { get; }
    public string Title { get; }
    public Color Background { get; }
    public Color Surface { get; }
    public Color Accent { get; }
    [ObservableProperty] private bool _isSelected;
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly MonitoringService _monitor;
    private readonly NotificationService _notifications;
    private bool _syncingTheme;

    private AutostartState _applied; // что реально настроено в системе сейчас

    public IReadOnlyList<ThemeOption> Themes { get; }

    public string ElevationText { get; } = AutostartService.IsProcessElevated()
        ? "Сейчас приложение работает с правами администратора: температура процессора доступна."
        : "Сейчас приложение работает без прав администратора: температура процессора может быть недоступна.";

    [ObservableProperty] private int _intervalSeconds;
    [ObservableProperty] private bool _saveHistory;
    [ObservableProperty] private int _retentionDays;
    [ObservableProperty] private int _historyIntervalSeconds;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _launchAtStartup;
    [ObservableProperty] private bool _startInTray;
    [ObservableProperty] private bool _runAsAdmin;
    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private bool _notificationSound;
    [ObservableProperty] private int _cpuThreshold;
    [ObservableProperty] private int _ramThreshold;
    [ObservableProperty] private int _gpuThreshold;
    [ObservableProperty] private int _tempThreshold;
    [ObservableProperty] private int _notifyCooldownMinutes;
    [ObservableProperty] private string _statusMessage = "";

    public SettingsViewModel(AppSettings settings, MonitoringService monitor, NotificationService notifications)
    {
        _settings = settings;
        _monitor = monitor;
        _notifications = notifications;
        Themes = ThemeService.Themes.Select(t => new ThemeOption(t)).ToList();
        LoadFromSettings();
        SyncThemeSelection();
        _ = LoadAutostartStateAsync();

        foreach (var t in Themes)
            t.PropertyChanged += (s, e) =>
            {
                if (_syncingTheme || e.PropertyName != nameof(ThemeOption.IsSelected)) return;
                var opt = (ThemeOption)s!;
                if (opt.IsSelected) ApplyTheme(opt.Name);
            };

        ThemeService.ThemeChanged += SyncThemeSelection; // быстрая смена темы из заголовка окна
    }

    private void LoadFromSettings()
    {
        IntervalSeconds = _settings.IntervalSeconds;
        SaveHistory = _settings.SaveHistory;
        RetentionDays = _settings.RetentionDays;
        HistoryIntervalSeconds = _settings.HistoryIntervalSeconds;
        CloseToTray = _settings.CloseToTray;
        MinimizeToTray = _settings.MinimizeToTray;
        StartInTray = _settings.StartInTray;
        LaunchAtStartup = AutostartService.IsEnabled(); // быстрый предварительный ответ из реестра
        _applied = new AutostartState(LaunchAtStartup, false);
        NotificationsEnabled = _settings.NotificationsEnabled;
        NotificationSound = _settings.NotificationSound;
        CpuThreshold = _settings.CpuThreshold;
        RamThreshold = _settings.RamThreshold;
        GpuThreshold = _settings.GpuThreshold;
        TempThreshold = _settings.TempThreshold;
        NotifyCooldownMinutes = _settings.NotifyCooldownMinutes;
    }

    private void SyncThemeSelection()
    {
        _syncingTheme = true;
        foreach (var t in Themes) t.IsSelected = t.Name == ThemeService.Current;
        _syncingTheme = false;
    }

    /// <summary>Тема применяется и сохраняется сразу — это предпросмотр без перезапуска.</summary>
    private void ApplyTheme(string name)
    {
        ThemeService.Apply(name);
        _settings.Theme = name;
        SettingsService.Save(_settings);
    }

    /// <summary>Уточняет состояние автозапуска по системе (в т.ч. задачу Планировщика) без блокировки UI.</summary>
    private async Task LoadAutostartStateAsync()
    {
        var state = await Task.Run(AutostartService.GetState);
        _applied = state;
        LaunchAtStartup = state.Enabled;
        RunAsAdmin = state.AsAdmin;
    }

    [RelayCommand]
    private void ShowTest() =>
        _notifications.Show("Проверка уведомлений", "Так выглядит предупреждение о превышении порога.", NotificationSound);

    [RelayCommand]
    private async Task SaveAsync()
    {
        _settings.IntervalSeconds = IntervalSeconds;
        _settings.SaveHistory = SaveHistory;
        _settings.RetentionDays = RetentionDays;
        _settings.HistoryIntervalSeconds = HistoryIntervalSeconds;
        _settings.CloseToTray = CloseToTray;
        _settings.MinimizeToTray = MinimizeToTray;
        _settings.StartInTray = StartInTray;
        _settings.NotificationsEnabled = NotificationsEnabled;
        _settings.NotificationSound = NotificationSound;
        _settings.CpuThreshold = CpuThreshold;
        _settings.RamThreshold = RamThreshold;
        _settings.GpuThreshold = GpuThreshold;
        _settings.TempThreshold = TempThreshold;
        _settings.NotifyCooldownMinutes = NotifyCooldownMinutes;
        SettingsService.Save(_settings);
        _monitor.SetInterval(IntervalSeconds);

        // Автозапуск меняем только если выбор отличается от того, что уже настроено в системе:
        // так окно UAC не появляется без необходимости.
        bool wantEnabled = LaunchAtStartup;
        bool wantAdmin = LaunchAtStartup && RunAsAdmin;
        string? autostartError = null;
        if (wantEnabled != _applied.Enabled || (wantEnabled && wantAdmin != _applied.AsAdmin))
        {
            StatusMessage = wantAdmin ? "Подтвердите запрос Windows (UAC)…" : "Применяю настройки автозапуска…";
            autostartError = await Task.Run(() => AutostartService.Apply(wantEnabled, wantAdmin));

            var actual = await Task.Run(AutostartService.GetState);
            _applied = actual;
            LaunchAtStartup = actual.Enabled; // при ошибке или отказе в UAC переключатели вернутся к реальному состоянию
            RunAsAdmin = actual.AsAdmin;
        }

        StatusMessage = autostartError ?? $"Настройки сохранены · {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand]
    private void Reset()
    {
        var d = new AppSettings();
        IntervalSeconds = d.IntervalSeconds;
        SaveHistory = d.SaveHistory;
        RetentionDays = d.RetentionDays;
        HistoryIntervalSeconds = d.HistoryIntervalSeconds;
        CloseToTray = d.CloseToTray;
        MinimizeToTray = d.MinimizeToTray;
        StartInTray = d.StartInTray;
        LaunchAtStartup = false;
        RunAsAdmin = false;
        NotificationsEnabled = d.NotificationsEnabled;
        NotificationSound = d.NotificationSound;
        CpuThreshold = d.CpuThreshold;
        RamThreshold = d.RamThreshold;
        GpuThreshold = d.GpuThreshold;
        TempThreshold = d.TempThreshold;
        NotifyCooldownMinutes = d.NotifyCooldownMinutes;
        StatusMessage = "Значения по умолчанию — нажмите «Сохранить», чтобы применить";
    }
}
