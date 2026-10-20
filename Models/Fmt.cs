namespace PcMonitor.Models;

public static class Fmt
{
    public static string Gb(double v) => v >= 1000 ? $"{v / 1024:0.##} ТБ" : $"{v:0.#} ГБ";

    public static Level LevelFor(double value, double threshold) =>
        value >= threshold ? Level.Critical : value >= threshold - 15 ? Level.Warning : Level.Normal;

    public static string StatusText(Level l) => l switch
    {
        Level.Critical => "Критично",
        Level.Warning => "Повышено",
        _ => "Норма",
    };
}
