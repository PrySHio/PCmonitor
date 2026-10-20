using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace PcMonitor.Services;

/// <summary>Показания аппаратных датчиков. null — датчик недоступен на этом ПК или нет прав.</summary>
public readonly record struct SensorReading(
    double? GpuLoad, double? VramUsedGb, double? VramTotalGb, double? CpuTemp, double? GpuTemp)
{
    public static SensorReading Empty => default;
}

/// <summary>
/// Обёртка над LibreHardwareMonitor. Инициализация идёт в фоне (занимает 1–3 с), пока она не завершена,
/// Read() сразу возвращает «нет данных» и не задерживает остальные показатели.
/// Температура CPU на большинстве систем требует прав администратора; GPU обычно читается и без них.
/// Любая ошибка датчиков превращается в «недоступно» (ТЗ 4.1.5, 4.1.12).
/// </summary>
public sealed class HardwareSensors : IDisposable
{
    private readonly object _gate = new();
    private Computer? _computer;
    private bool _disposed;

    /// <summary>Причина, по которой датчики не открылись (для диагностики), иначе null.</summary>
    public string? Error { get; private set; }

    public HardwareSensors() => _ = Task.Run(Open);

    private void Open()
    {
        try
        {
            var c = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            c.Open();
            lock (_gate)
            {
                if (_disposed) { c.Close(); return; }
                _computer = c;
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    public SensorReading Read()
    {
        lock (_gate)
        {
            if (_computer is null || _disposed) return SensorReading.Empty;
            try
            {
                foreach (var hw in _computer.Hardware)
                    foreach (var h in Flatten(hw))
                        h.Update();

                var (gpuLoad, used, total, gpuTemp) = ReadGpu(_computer);
                return new SensorReading(gpuLoad, used, total, ReadCpuTemp(_computer), gpuTemp);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return SensorReading.Empty;
            }
        }
    }

    // ── GPU ────────────────────────────────────────────────────────────────
    private static (double? load, double? usedGb, double? totalGb, double? temp) ReadGpu(Computer c)
    {
        var gpus = c.Hardware
            .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .ToList();
        // дискретная карта важнее встроенной
        var gpu = gpus.FirstOrDefault(h => h.HardwareType != HardwareType.GpuIntel) ?? gpus.FirstOrDefault();
        if (gpu is null) return (null, null, null, null);

        var sensors = Flatten(gpu).SelectMany(h => h.Sensors).ToList();

        double? load = Pick(sensors, SensorType.Load, "GPU Core", "D3D 3D");
        double? temp = Pick(sensors, SensorType.Temperature, "GPU Core", "GPU Hot Spot");
        double? usedMb = Pick(sensors, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
        double? totalMb = Pick(sensors, SensorType.SmallData, "GPU Memory Total");

        return (load is null ? null : Math.Clamp(load.Value, 0, 100),
                usedMb / 1024, totalMb is > 0 ? totalMb / 1024 : null, temp);
    }

    /// <summary>Значение первого найденного датчика из списка предпочтительных имён.</summary>
    private static double? Pick(List<ISensor> sensors, SensorType type, params string[] names)
    {
        foreach (var name in names)
        {
            var s = sensors.FirstOrDefault(x => x.SensorType == type && x.Name == name && x.Value.HasValue);
            if (s is not null) return s.Value!.Value;
        }
        return null;
    }

    // ── Температура процессора ─────────────────────────────────────────────
    private static double? ReadCpuTemp(Computer c)
    {
        var temps = c.Hardware
            .Where(h => h.HardwareType == HardwareType.Cpu)
            .SelectMany(Flatten).SelectMany(h => h.Sensors)
            .Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0 and < 150)
            .ToList();
        if (temps.Count == 0) return null;

        // Intel: «CPU Package», AMD: «Core (Tctl/Tdie)»; иначе — самое горячее ядро
        var best = temps.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                ?? temps.FirstOrDefault(s => s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
                                          || s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase))
                ?? temps.FirstOrDefault(s => s.Name.Contains("Max", StringComparison.OrdinalIgnoreCase));
        return best?.Value ?? temps.Max(s => s.Value!.Value);
    }

    private static IEnumerable<IHardware> Flatten(IHardware h)
    {
        yield return h;
        foreach (var sub in h.SubHardware)
            foreach (var x in Flatten(sub))
                yield return x;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _computer?.Close(); } catch { }
            _computer = null;
        }
    }
}
