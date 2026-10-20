using PcMonitor.Services;

namespace PcMonitor.Models;

public static class Fmt
{
    public static string Gb(double v) =>
        v >= 1000 ? $"{v / 1024:0.##} {Loc.T("Unit_Tb")}" : $"{v:0.#} {Loc.T("Unit_Gb")}";

    public static Level LevelFor(double value, double threshold) =>
        value >= threshold ? Level.Critical : value >= threshold - 15 ? Level.Warning : Level.Normal;

    public static string StatusText(Level l) => l switch
    {
        Level.Critical => Loc.T("Status_Critical"),
        Level.Warning => Loc.T("Status_Warning"),
        _ => Loc.T("Status_Normal"),
    };
}
