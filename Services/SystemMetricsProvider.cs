using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using PcMonitor.Models;

namespace PcMonitor.Services;

/// <summary>
/// Реальные показатели Windows без внешних пакетов и прав администратора (ТЗ 4.3.2):
/// CPU — GetSystemTimes, RAM — GlobalMemoryStatusEx, диски — DriveInfo.
/// GPU и температуры — через LibreHardwareMonitor (HardwareSensors); если датчика нет, значение остаётся «недоступно».
/// Read() вызывается из фонового потока и не трогает UI.
/// </summary>
public sealed class SystemMetricsProvider : IMetricsProvider, IDisposable
{
    private const double Gib = 1024.0 * 1024 * 1024;

    private readonly HardwareSensors _sensors = new();
    private long _prevIdle, _prevKernel, _prevUser;
    private double _lastCpu;

    public SystemMetricsProvider()
    {
        GetSystemTimes(out _prevIdle, out _prevKernel, out _prevUser);
    }

    public string SourceName => "Windows + LibreHardwareMonitor";
    public bool IsDemo => false;

    public MetricsSnapshot Read()
    {
        var cpu = ReadCpu();
        var (usedGb, totalGb) = ReadMemory();
        var disks = ReadDisks();
        var hw = _sensors.Read(); // GPU и температуры; пока датчики инициализируются — «нет данных»
        return new MetricsSnapshot(DateTime.Now, cpu, usedGb, totalGb,
            hw.GpuLoad, hw.VramUsedGb, hw.VramTotalGb, hw.CpuTemp, hw.GpuTemp, disks);
    }

    public void Dispose() => _sensors.Dispose();

    // ── CPU ────────────────────────────────────────────────────────────────
    private double ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return _lastCpu;

        long dIdle = idle - _prevIdle, dKernel = kernel - _prevKernel, dUser = user - _prevUser;
        _prevIdle = idle; _prevKernel = kernel; _prevUser = user;

        long total = dKernel + dUser; // kernel уже включает idle
        if (total <= 0) return _lastCpu;

        _lastCpu = Math.Clamp((total - dIdle) * 100.0 / total, 0, 100);
        return _lastCpu;
    }

    // ── RAM ────────────────────────────────────────────────────────────────
    private static (double usedGb, double totalGb) ReadMemory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return (0, 0);
        return ((m.ullTotalPhys - m.ullAvailPhys) / Gib, m.ullTotalPhys / Gib);
    }

    // ── Диски ──────────────────────────────────────────────────────────────
    private static List<DiskInfo> ReadDisks()
    {
        var result = new List<DiskInfo>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                double total = d.TotalSize / Gib;
                double free = d.AvailableFreeSpace / Gib;
                var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? Loc.T("HW_LocalDisk") : d.VolumeLabel;
                result.Add(new DiskInfo(d.Name.TrimEnd('\\'), label, total, total - free));
            }
            catch
            {
                // один недоступный диск не должен ломать остальные (ТЗ 4.1.12)
            }
        }
        return result;
    }

    // ── Сведения о компонентах ─────────────────────────────────────────────
    public IReadOnlyList<ComponentInfo> GetComponents()
    {
        var (_, totalGb) = ReadMemory();
        var os = Environment.OSVersion.Version;
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        var list = new List<ComponentInfo>
        {
            HardwareInfo.Cpu(CpuName(), CpuMhz()),
            HardwareInfo.Ram(totalGb),
        };
        list.AddRange(HardwareInfo.Gpus());
        string d = Loc.T("Unit_DayShort"), h = Loc.T("Unit_HourShort"), min = Loc.T("Unit_Min");
        list.Add(new ComponentInfo(Loc.T("Nav_System"), "IconDashboard", new KeyValuePair<string, string>[]
        {
            new(Loc.T("HW_Os"), os.Build >= 22000 ? "Windows 11" : "Windows 10"),
            new(Loc.T("HW_Build"), os.Build.ToString()),
            new(Loc.T("HW_Uptime"), uptime.TotalDays >= 1
                ? $"{(int)uptime.TotalDays} {d} {uptime.Hours} {h}"
                : $"{uptime.Hours} {h} {uptime.Minutes} {min}"),
            new(Loc.T("HW_Admin"), Loc.T(IsAdmin() ? "Yes" : "No")),
        }));
        return list;
    }

    private static string CpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var name = (key?.GetValue("ProcessorNameString") as string)?.Trim();
            return string.IsNullOrEmpty(name) ? Loc.T("NA") : name;
        }
        catch { return Loc.T("NA"); }
    }

    private static int? CpuMhz()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("~MHz") as int?;
        }
        catch { return null; }
    }

    private static bool IsAdmin()
    {
        try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    // ── WinAPI ─────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
