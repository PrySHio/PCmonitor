using System.Windows;
using System.Windows.Media;

namespace PcMonitor.Services;

/// <summary>Описание темы для выбора в настройках. Название берётся из словаря локализации по ключу Theme_{Name}.</summary>
public sealed record ThemeInfo(string Name, Color Background, Color Surface, Color Accent);

public static class ThemeService
{
    public static IReadOnlyList<ThemeInfo> Themes { get; } = new[]
    {
        new ThemeInfo("Dark", Color.FromRgb(0x0F, 0x11, 0x15), Color.FromRgb(0x1A, 0x1E, 0x25), Color.FromRgb(0x6C, 0x8C, 0xFF)),
        new ThemeInfo("Light", Color.FromRgb(0xF3, 0xF5, 0xF9), Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x4F, 0x6B, 0xED)),
        new ThemeInfo("Ocean", Color.FromRgb(0x08, 0x16, 0x1F), Color.FromRgb(0x10, 0x28, 0x3A), Color.FromRgb(0x2E, 0xC5, 0xE8)),
        new ThemeInfo("Violet", Color.FromRgb(0x12, 0x0D, 0x1C), Color.FromRgb(0x1F, 0x17, 0x33), Color.FromRgb(0xA9, 0x78, 0xFF)),
        new ThemeInfo("Emerald", Color.FromRgb(0x0A, 0x15, 0x11), Color.FromRgb(0x14, 0x27, 0x20), Color.FromRgb(0x34, 0xD3, 0x99)),
    };

    public static string Current { get; private set; } = "Dark";

    /// <summary>Вызывается после смены темы — нужен контролам, которые рисуют сами (графики).</summary>
    public static event Action? ThemeChanged;

    /// <summary>Заменяет словарь темы (всегда первый в MergedDictionaries) без перезапуска.</summary>
    public static void Apply(string name)
    {
        if (!Themes.Any(t => t.Name == name))
            name = "Dark";

        var dict = new ResourceDictionary { Source = new Uri($"Themes/{name}.xaml", UriKind.Relative) };
        var merged = Application.Current.Resources.MergedDictionaries;

        if (merged.Count > 0)
            merged[0] = dict;
        else
            merged.Add(dict);

        Current = name;
        ThemeChanged?.Invoke();
    }
}
