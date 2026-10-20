using PcMonitor.Models;

namespace PcMonitor.Services;

/// <summary>Генерирует правдоподобные плавные значения для разработки UI. GPU-температура намеренно недоступна — чтобы показать состояние «Недоступно».</summary>
public sealed class DemoMetricsProvider : IMetricsProvider
{
    private readonly Random _rnd = new(42);
    private double _cpu = 28, _ram = 52, _gpu = 18, _cpuT = 48;
    private double _phase;

    public string SourceName => "Демо-данные";
    public bool IsDemo => true;

    public MetricsSnapshot Read()
    {
        _phase += 0.12;
        _cpu = Walk(_cpu, 4 + 22 * (Math.Sin(_phase) + 1) / 2 + _rnd.NextDouble() * 10, 0.35, 2, 98);
        _ram = Walk(_ram, 54 + 6 * Math.Sin(_phase / 3), 0.1, 20, 95);
        _gpu = Walk(_gpu, 12 + 40 * (Math.Sin(_phase / 2 + 1) + 1) / 2, 0.3, 1, 99);
        _cpuT = Walk(_cpuT, 38 + _cpu * 0.45, 0.2, 30, 95);

        const double ramTotal = 32, vramTotal = 12;
        var disks = new[]
        {
            new DiskInfo("C:", "Windows", 476.9, 312.4),
            new DiskInfo("D:", "Данные", 931.5, 402.7),
            new DiskInfo("E:", "Игры", 1862.9, 1701.3),
        };

        return new MetricsSnapshot(
            DateTime.Now, _cpu,
            ramTotal * _ram / 100, ramTotal,
            _gpu, vramTotal * (0.2 + _gpu / 160), vramTotal,
            _cpuT, null, disks);
    }

    public IReadOnlyList<ComponentInfo> GetComponents() => new[]
    {
        new ComponentInfo("Процессор", "IconCpu", new KeyValuePair<string, string>[]
        {
            new("Модель", "AMD Ryzen 7 5800X"),
            new("Ядра / потоки", "8 / 16"),
            new("Базовая частота", "3,8 ГГц"),
            new("Сокет", "AM4"),
        }),
        new ComponentInfo("Оперативная память", "IconRam", new KeyValuePair<string, string>[]
        {
            new("Объём", "32 ГБ"),
            new("Тип", "DDR4"),
            new("Частота", "3600 МГц"),
            new("Модули", "2 × 16 ГБ"),
        }),
        new ComponentInfo("Видеокарта", "IconGpu", new KeyValuePair<string, string>[]
        {
            new("Модель", "NVIDIA GeForce RTX 3060"),
            new("Видеопамять", "12 ГБ GDDR6"),
            new("Драйвер", "560.94"),
            new("Температура", "Недоступно"),
        }),
        new ComponentInfo("Система", "IconDashboard", new KeyValuePair<string, string>[]
        {
            new("ОС", "Windows 11 Pro"),
            new("Сборка", "22631"),
            new("Время работы", "3 д 14 ч"),
            new("Права администратора", "Нет"),
        }),
    };

    private double Walk(double cur, double target, double k, double min, double max)
    {
        var next = cur + (target - cur) * k + (_rnd.NextDouble() - 0.5) * 2.2;
        return Math.Clamp(next, min, max);
    }
}
