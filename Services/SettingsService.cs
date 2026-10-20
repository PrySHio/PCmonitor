using System.IO;
using System.Text.Json;

namespace PcMonitor.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    /// <summary>Язык интерфейса: "ru", "en" или пусто (определяется по языку Windows при первом запуске).</summary>
    public string Language { get; set; } = "";
    public int IntervalSeconds { get; set; } = 1;
    public bool SaveHistory { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool NotificationSound { get; set; } = true;
    public int CpuThreshold { get; set; } = 90;
    public int RamThreshold { get; set; } = 90;
    public int GpuThreshold { get; set; } = 90;
    public int TempThreshold { get; set; } = 85;
    public int NotifyCooldownMinutes { get; set; } = 5;
    public int RetentionDays { get; set; } = 30;
    public int HistoryIntervalSeconds { get; set; } = 5;

    // Поведение приложения (всё выключено по умолчанию)
    public bool CloseToTray { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool StartInTray { get; set; }
    public bool TrayHintShown { get; set; }
}

/// <summary>Хранит настройки в %AppData%\PcMonitor\settings.json. Ошибки чтения не фатальны.</summary>
public static class SettingsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PcMonitor", "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // повреждённый файл — используем значения по умолчанию
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // ошибка записи не должна ронять приложение
        }
    }
}
