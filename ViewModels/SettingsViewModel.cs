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
        Name = info.Name;
        Background = info.Background; Surface = info.Surface; Accent = info.Accent;
    }
    public string Name { get; }
    public string Title => Loc.T("Theme_" + Name);
    public void Refresh() => OnPropertyChanged(nameof(Title));
    public Color Background { get; }
    public Color Surface { get; }
    public Color Accent { get; }
    [ObservableProperty] private bool _isSelected;
}

public partial class LanguageOption : ObservableObject
{
    public LanguageOption(LanguageInfo info) { Code = info.Code; Title = info.Title; }
    public string Code { get; }
    public string Title { get; }
    [ObservableProperty] private bool _isSelected;
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly MonitoringService _monitor;
    private readonly NotificationService _notifications;
    private bool _syncingTheme;
    private bool _syncingLanguage;

    private AutostartState _applied; // что реально настроено в системе сейчас

    public IReadOnlyList<ThemeOption> Themes { get; }
    public IReadOnlyList<LanguageOption> Languages { get; }

    public string ElevationText =>
        Loc.T(AutostartService.IsProcessElevated() ? "Settings_ElevatedYes" : "Settings_ElevatedNo");

    // Значения с единицами измерения собираются здесь, а не в XAML, чтобы единицы переводились вместе с языком
    public string IntervalText => $"{IntervalSeconds} {Loc.T("Unit_Sec")}";
    public string HistoryIntervalText => $"{HistoryIntervalSeconds} {Loc.T("Unit_Sec")}";
    public string RetentionText => $"{RetentionDays} {Loc.T("Unit_Days")}";
    public string TempThresholdText => $"{TempThreshold} °C";
    public string CooldownText => $"{NotifyCooldownMinutes} {Loc.T("Unit_Min")}";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IntervalText))] private int _intervalSeconds;
    [ObservableProperty] private bool _saveHistory;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RetentionText))] private int _retentionDays;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HistoryIntervalText))] private int _historyIntervalSeconds;
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
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TempThresholdText))] private int _tempThreshold;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CooldownText))] private int _notifyCooldownMinutes;
    [ObservableProperty] private string _statusMessage = "";

    public SettingsViewModel(AppSettings settings, MonitoringService monitor, NotificationService notifications)
    {
        _settings = settings;
        _monitor = monitor;
        _notifications = notifications;
        Themes = ThemeService.Themes.Select(t => new ThemeOption(t)).ToList();
        Languages = Loc.Languages.Select(l => new LanguageOption(l)).ToList();
        LoadFromSettings();
        SyncThemeSelection();
        SyncLanguageSelection();
        _ = LoadAutostartStateAsync();

        foreach (var t in Themes)
            t.PropertyChanged += (s, e) =>
            {
                if (_syncingTheme || e.PropertyName != nameof(ThemeOption.IsSelected)) return;
                var opt = (ThemeOption)s!;
                if (opt.IsSelected) ApplyTheme(opt.Name);
            };

        foreach (var l in Languages)
            l.PropertyChanged += (s, e) =>
            {
                if (_syncingLanguage || e.PropertyName != nameof(LanguageOption.IsSelected)) return;
                var opt = (LanguageOption)s!;
                if (opt.IsSelected) ApplyLanguage(opt.Code);
            };

        ThemeService.ThemeChanged += SyncThemeSelection; // быстрая смена темы из заголовка окна
        Loc.LanguageChanged += OnLanguageChanged;
    }

    private void SyncLanguageSelection()
    {
        _syncingLanguage = true;
        foreach (var l in Languages) l.IsSelected = l.Code == Loc.Current;
        _syncingLanguage = false;
    }

    /// <summary>Язык применяется и сохраняется сразу, как и тема: перезапуск не нужен.</summary>
    private void ApplyLanguage(string code)
    {
        Loc.SetLanguage(code);
        _settings.Language = code;
        SettingsService.Save(_settings);
    }

    private void OnLanguageChanged()
    {
        foreach (var t in Themes) t.Refresh();
        OnPropertyChanged(nameof(ElevationText));
        OnPropertyChanged(nameof(IntervalText));
        OnPropertyChanged(nameof(HistoryIntervalText));
        OnPropertyChanged(nameof(RetentionText));
        OnPropertyChanged(nameof(TempThresholdText));
        OnPropertyChanged(nameof(CooldownText));
        StatusMessage = "";
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
        _notifications.Show(Loc.T("Settings_TestTitle"), Loc.T("Settings_TestText"), NotificationSound);

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
            StatusMessage = Loc.T(wantAdmin ? "Settings_UacPrompt" : "Settings_Applying");
            autostartError = await Task.Run(() => AutostartService.Apply(wantEnabled, wantAdmin));

            var actual = await Task.Run(AutostartService.GetState);
            _applied = actual;
            LaunchAtStartup = actual.Enabled; // при ошибке или отказе в UAC переключатели вернутся к реальному состоянию
            RunAsAdmin = actual.AsAdmin;
        }

        StatusMessage = autostartError ?? Loc.F("Settings_Saved", DateTime.Now);
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
        StatusMessage = Loc.T("Settings_ResetDone");
    }
}
