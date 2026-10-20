using System.Management;
using Microsoft.Win32;
using PcMonitor.Models;

namespace PcMonitor.Services;

/// <summary>
/// Статические сведения об оборудовании (WMI + реестр). Права администратора не нужны.
/// Любой сбой отдельного запроса превращается в «Недоступно», остальное продолжает работать.
/// Вызывать из фонового потока: WMI-запросы могут занимать сотни миллисекунд.
/// </summary>
internal static class HardwareInfo
{
    private const string NA = "Недоступно";
    private const double Gib = 1024.0 * 1024 * 1024;

    // ── Процессор ──────────────────────────────────────────────────────────
    public static ComponentInfo Cpu(string fallbackName, int? fallbackMhz)
    {
        string? name = null;
        int? cores = null, threads = null, mhz = fallbackMhz;
        try
        {
            var rows = Query("SELECT * FROM Win32_Processor", o => (
                Name: Str(P(o, "Name")),
                Cores: Int(P(o, "NumberOfCores")),
                Threads: Int(P(o, "NumberOfLogicalProcessors")),
                Mhz: Int(P(o, "MaxClockSpeed"))));
            if (rows.Count > 0)
            {
                name = rows[0].Name;
                cores = rows.Sum(r => r.Cores ?? 0);
                threads = rows.Sum(r => r.Threads ?? 0);
                mhz = rows[0].Mhz ?? mhz;
                if (cores == 0) cores = null;
                if (threads == 0) threads = null;
            }
        }
        catch { /* оставляем запасные значения */ }

        name ??= fallbackName;
        threads ??= Environment.ProcessorCount;

        return new ComponentInfo("Процессор", "IconCpu", new[]
        {
            Row("Модель", name),
            Row("Ядра / потоки", cores is int c ? $"{c} / {threads}" : $"{threads} потоков"),
            Row("Частота (макс.)", mhz is int m && m > 0 ? $"{m / 1000.0:0.0#} ГГц" : null),
        });
    }

    // ── Оперативная память ─────────────────────────────────────────────────
    public static ComponentInfo Ram(double fallbackTotalGb)
    {
        var mods = new List<(ulong Cap, int? Speed, int? Type)>();
        int? slots = null;
        try
        {
            mods = Query("SELECT * FROM Win32_PhysicalMemory", o => (
                Cap: ULong(P(o, "Capacity")) ?? 0UL,
                Speed: Int(P(o, "ConfiguredClockSpeed")) ?? Int(P(o, "Speed")),
                Type: Int(P(o, "SMBIOSMemoryType"))));
        }
        catch { }
        try
        {
            slots = Query("SELECT * FROM Win32_PhysicalMemoryArray", o => Int(P(o, "MemoryDevices")))
                .FirstOrDefault(s => s is > 0);
        }
        catch { }

        double totalGb = mods.Sum(m => (double)m.Cap) / Gib;
        if (totalGb <= 0) totalGb = Math.Round(fallbackTotalGb);

        var speed = mods.Select(m => m.Speed).FirstOrDefault(s => s is > 0);
        var type = MemType(mods.Select(m => m.Type).FirstOrDefault(t => t is > 0));

        string? moduleText = mods.Count == 0
            ? null
            : string.Join(" + ", mods.GroupBy(m => m.Cap).Select(g => $"{g.Count()} × {Fmt.Gb(g.Key / Gib)}"));
        string? slotText = mods.Count > 0 && slots is int s2 ? $"{mods.Count} из {s2}" : null;

        return new ComponentInfo("Оперативная память", "IconRam", new[]
        {
            Row("Объём", totalGb > 0 ? Fmt.Gb(totalGb) : null),
            Row("Тип", type),
            Row("Частота", speed is int sp ? $"{sp} МГц" : null),
            Row("Модули", moduleText),
            Row("Занято слотов", slotText),
        });
    }

    // ── Видеокарты ─────────────────────────────────────────────────────────
    public static List<ComponentInfo> Gpus()
    {
        var result = new List<ComponentInfo>();
        try
        {
            var all = Query("SELECT * FROM Win32_VideoController", o => (
                Name: Str(P(o, "Name")),
                Driver: Str(P(o, "DriverVersion")),
                Ram: ULong(P(o, "AdapterRAM")),
                Pnp: Str(P(o, "PNPDeviceID"))));

            // виртуальные адаптеры (удалённый рабочий стол и т.п.) не относятся к PCI
            var phys = all.Where(a => a.Pnp?.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) == true).ToList();
            if (phys.Count == 0) phys = all;

            for (int i = 0; i < phys.Count; i++)
            {
                var a = phys[i];
                ulong? vram = RegistryVram(a.Name);
                if (vram is null or 0) vram = a.Ram; // у WMI поле 32-битное: для карт >4 ГБ занижает

                result.Add(new ComponentInfo(
                    phys.Count > 1 ? $"Видеокарта {i + 1}" : "Видеокарта", "IconGpu", new[]
                    {
                        Row("Модель", a.Name),
                        Row("Видеопамять", vram is > 0 ? Fmt.Gb((double)vram.Value / Gib) : null),
                        Row("Драйвер", Driver(a.Name, a.Driver)),
                    }));
            }
        }
        catch { }

        if (result.Count == 0)
            result.Add(new ComponentInfo("Видеокарта", "IconGpu", new[]
            {
                Row("Модель", null), Row("Видеопамять", null), Row("Драйвер", null),
            }));
        return result;
    }

    /// <summary>Точный объём видеопамяти из реестра (64-битное значение, в отличие от WMI AdapterRAM).</summary>
    private static ulong? RegistryVram(string? name)
    {
        if (name is null) return null;
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null) return null;

            foreach (var sub in cls.GetSubKeyNames())
            {
                if (!sub.All(char.IsDigit)) continue;
                using var key = cls.OpenSubKey(sub);
                if (key is null) continue;
                if (!string.Equals(key.GetValue("DriverDesc") as string, name, StringComparison.OrdinalIgnoreCase)) continue;

                ulong? r = key.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l when l > 0 => (ulong)l,
                    ulong u => u,
                    int n when n > 0 => (ulong)n,
                    byte[] b when b.Length >= 8 => BitConverter.ToUInt64(b, 0),
                    byte[] b4 when b4.Length == 4 => BitConverter.ToUInt32(b4, 0),
                    _ => null,
                };
                if (r is > 0) return r;
            }
        }
        catch { }
        return null;
    }

    /// <summary>У NVIDIA версия драйвера в WMI вида 32.0.15.6094; привычный номер — 560.94.</summary>
    private static string? Driver(string? name, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (name?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) == true)
        {
            var p = raw.Split('.');
            if (p.Length == 4)
            {
                var digits = p[2] + p[3];
                if (digits.Length >= 5)
                {
                    digits = digits[^5..];
                    return $"{digits[..3]}.{digits[3..]}";
                }
            }
        }
        return raw;
    }

    // ── Вспомогательное ────────────────────────────────────────────────────
    private static string? MemType(int? t) => t switch
    {
        20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR",
        28 => "LPDDR2", 29 => "LPDDR3", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5",
        _ => null,
    };

    private static KeyValuePair<string, string> Row(string key, string? value) =>
        new(key, string.IsNullOrWhiteSpace(value) ? NA : value);

    private static List<T> Query<T>(string wql, Func<ManagementBaseObject, T> map)
    {
        var list = new List<T>();
        using var searcher = new ManagementObjectSearcher(wql);
        foreach (ManagementBaseObject o in searcher.Get())
        {
            using (o) list.Add(map(o));
        }
        return list;
    }

    private static object? P(ManagementBaseObject o, string name)
    {
        try { return o[name]; } catch { return null; }
    }

    private static string? Str(object? v) => v?.ToString()?.Trim();
    private static int? Int(object? v) => v is null ? null : Convert.ToInt32(v);
    private static ulong? ULong(object? v) => v is null ? null : Convert.ToUInt64(v);
}
