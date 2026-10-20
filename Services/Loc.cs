using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace PcMonitor.Services;

public sealed record LanguageInfo(string Code, string Title);

/// <summary>
/// Локализация интерфейса (русский / английский). Язык меняется на лету: XAML-тексты обновляются через
/// {l:Tr Ключ}, а тексты, собираемые в коде, перечитывают модели представления по событию LanguageChanged.
/// </summary>
public static class Loc
{
    /// <summary>Названия языков записаны на самих языках — так их находят, даже если интерфейс непонятен.</summary>
    public static IReadOnlyList<LanguageInfo> Languages { get; } = new[]
    {
        new LanguageInfo("ru", "Русский"),
        new LanguageInfo("en", "English"),
    };

    public static string Current { get; private set; } = "ru";

    public static event Action? LanguageChanged;

    /// <summary>Язык по умолчанию: русский, если язык интерфейса Windows русский, иначе английский.</summary>
    public static string Detect() =>
        CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";

    /// <summary>Нормализует сохранённое значение настройки (пустое или неизвестное — автоопределение).</summary>
    public static string Resolve(string? code) =>
        Languages.Any(l => l.Code == code) ? code! : Detect();

    /// <summary>Устанавливает язык до показа окна (событие не вызывается).</summary>
    public static void Initialize(string? code) => Apply(Resolve(code));

    /// <summary>Переключает язык во время работы и оповещает подписчиков.</summary>
    public static void SetLanguage(string code)
    {
        code = Resolve(code);
        if (code == Current) return;
        Apply(code);
        LocSource.Instance.Refresh();
        LanguageChanged?.Invoke();
    }

    private static void Apply(string code)
    {
        Current = code;

        // числа и даты форматируются по выбранному языку (запятая или точка), в том числе в фоновых потоках
        var culture = CultureInfo.GetCultureInfo(code == "ru" ? "ru-RU" : "en-US");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (Application.Current?.MainWindow is { } window)
            window.Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
    }

    public static string T(string key)
    {
        var table = Current == "en" ? Strings.En : Strings.Ru;
        if (table.TryGetValue(key, out var text)) return text;
        return Strings.Ru.TryGetValue(key, out text) ? text : key;
    }

    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);
}

/// <summary>Источник для привязок в XAML: индексатор возвращает текст по ключу и обновляется при смене языка.</summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    public string this[string key] => Loc.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}

/// <summary>Разметка {l:Tr Ключ}: текст из словаря, который перерисовывается при смене языка.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = LocSource.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
