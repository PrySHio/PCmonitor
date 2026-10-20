using PcMonitor.Models;

namespace PcMonitor.Services;

/// <summary>Генерирует правдоподобные плавные значения для разработки UI. GPU-температура намеренно недоступна — чтобы показать состояние «Недоступно».</summary>
public sealed class DemoMetricsProvider : IMetricsProvider
{
    private readonly Random _rnd = new(42);
    private double _cpu = 28, _ram = 52, _gpu = 18, _cpuT = 48;
    private double _phase;

    public string SourceName => Loc.T("Demo_Source");
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
            new DiskInfo("D:", Loc.T("Demo_Data"), 931.5, 402.7),
            new DiskInfo("E:", Loc.T("Demo_Games"), 1862.9, 1701.3),
        };

        return new MetricsSnapshot(
            DateTime.Now, _cpu,
            ramTotal * _ram / 100, ramTotal,
            _gpu, vramTotal * (0.2 + _gpu / 160), vramTotal,
            _cpuT, null, disks);
    }

    public IReadOnlyList<ComponentInfo> GetComponents()
    {
        string gb = Loc.T("Unit_Gb"), ghz = Loc.T("Unit_Ghz"), mhz = Loc.T("Unit_Mhz");
        return new[]
        {
            new ComponentInfo(Loc.T("Card_Processor"), "IconCpu", new KeyValuePair<string, string>[]
            {
                new(Loc.T("HW_Model"), "AMD Ryzen 7 5800X"),
                new(Loc.T("HW_CoresThreads"), "8 / 16"),
                new(Loc.T("HW_BaseFreq"), $"{3.8:0.0} {ghz}"),
                new(Loc.T("HW_Socket"), "AM4"),
            }),
            new ComponentInfo(Loc.T("Card_Ram"), "IconRam", new KeyValuePair<string, string>[]
            {
                new(Loc.T("HW_Capacity"), $"32 {gb}"),
                new(Loc.T("HW_Type"), "DDR4"),
                new(Loc.T("HW_Speed"), $"3600 {mhz}"),
                new(Loc.T("HW_Modules"), $"2 × 16 {gb}"),
            }),
            new ComponentInfo(Loc.T("Card_Gpu"), "IconGpu", new KeyValuePair<string, string>[]
            {
                new(Loc.T("HW_Model"), "NVIDIA GeForce RTX 3060"),
                new(Loc.T("HW_Vram"), $"12 {gb} GDDR6"),
                new(Loc.T("HW_Driver"), "560.94"),
                new(Loc.T("HW_Temp"), Loc.T("NA")),
            }),
            new ComponentInfo(Loc.T("Nav_System"), "IconDashboard", new KeyValuePair<string, string>[]
            {
                new(Loc.T("HW_Os"), "Windows 11 Pro"),
                new(Loc.T("HW_Build"), "22631"),
                new(Loc.T("HW_Uptime"), $"3 {Loc.T("Unit_DayShort")} 14 {Loc.T("Unit_HourShort")}"),
                new(Loc.T("HW_Admin"), Loc.T("No")),
            }),
        };
    }

    private double Walk(double cur, double target, double k, double min, double max)
    {
        var next = cur + (target - cur) * k + (_rnd.NextDouble() - 0.5) * 2.2;
        return Math.Clamp(next, min, max);
    }
}
