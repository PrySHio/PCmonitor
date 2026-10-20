using System.Threading.Tasks;
using System.Windows.Threading;
using PcMonitor.Models;

namespace PcMonitor.Services;

/// <summary>
/// Периодически опрашивает провайдер и хранит скользящие буферы для графиков реального времени.
/// В демо-режиме работает на UI-таймере; для реального провайдера опрос переносится в фоновый поток.
/// </summary>
public sealed class MonitoringService
{
    public const int BufferSize = 120; // последние 120 с при интервале 1 с (ТЗ 4.1.7)

    private readonly IMetricsProvider _provider;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, List<double>> _buffers = new()
    {
        [MetricKeys.Cpu] = new(), [MetricKeys.Ram] = new(), [MetricKeys.Gpu] = new(),
        [MetricKeys.CpuTemp] = new(), [MetricKeys.GpuTemp] = new(),
    };

    public MonitoringService(IMetricsProvider provider, int intervalSeconds)
    {
        _provider = provider;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 1, 10)) };
        _timer.Tick += (_, _) => Tick();
    }

    public IMetricsProvider Provider => _provider;
    public MetricsSnapshot? Latest { get; private set; }
    public int IntervalSeconds => (int)_timer.Interval.TotalSeconds;

    public event Action<MetricsSnapshot>? Updated;

    public void Start()
    {
        Tick();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void SetInterval(int seconds) => _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 10));

    /// <summary>Копия буфера — безопасна для передачи в контрол.</summary>
    public double[] GetSeries(string key, int maxPoints)
    {
        var list = _buffers[key];
        var skip = Math.Max(0, list.Count - maxPoints);
        return list.Skip(skip).ToArray();
    }

    private bool _busy;

    /// <summary>Опрос идёт в фоновом потоке (ТЗ 4.2.2); результат обрабатывается в UI-потоке.</summary>
    private async void Tick()
    {
        if (_busy) return; // предыдущий опрос ещё не завершился — пропускаем цикл
        _busy = true;

        MetricsSnapshot snap;
        try
        {
            snap = await Task.Run(() => _provider.Read());
        }
        catch
        {
            return; // сбой одного цикла не останавливает мониторинг (ТЗ 4.1.12)
        }
        finally
        {
            _busy = false;
        }

        Latest = snap;
        Push(MetricKeys.Cpu, snap.Cpu);
        Push(MetricKeys.Ram, snap.RamPercent);
        Push(MetricKeys.Gpu, snap.GpuLoad);
        Push(MetricKeys.CpuTemp, snap.CpuTemp);
        Push(MetricKeys.GpuTemp, snap.GpuTemp);
        Updated?.Invoke(snap);
    }

    private void Push(string key, double? value)
    {
        var list = _buffers[key];
        if (value is null) { list.Clear(); return; } // нет данных — график пуст, ничего не выдумываем
        list.Add(value.Value);
        if (list.Count > BufferSize) list.RemoveAt(0);
    }
}
