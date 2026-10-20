using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using PcMonitor.Data;
using PcMonitor.Models;

namespace PcMonitor.Services;

/// <summary>
/// Записывает показатели в историю: копит замеры в памяти и сбрасывает пакетами в фоне, чтобы не нагружать UI и диск.
/// Следит за периодом записи, сроком хранения и сообщает об ошибках БД, не прерывая мониторинг (ТЗ 4.1.8, 4.1.12, 4.7.4).
/// </summary>
public sealed class HistoryRecorder
{
    private const int MaxQueue = 20_000; // защита памяти, если БД долго недоступна

    private readonly IHistoryRepository _repo;
    private readonly MonitoringService _monitor;
    private readonly AppSettings _settings;
    private readonly ConcurrentQueue<MetricRecord> _queue = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private System.Threading.Timer? _timer;
    private DateTime _lastSavedUtc = DateTime.MinValue;
    private int _ticks;
    private bool _hadError;

    public HistoryRecorder(IHistoryRepository repo, MonitoringService monitor, AppSettings settings)
    {
        _repo = repo;
        _monitor = monitor;
        _settings = settings;
    }

    /// <summary>Текст ошибки или null, когда всё в порядке. Вызывается из фонового потока.</summary>
    public event Action<string?>? StatusChanged;

    public void Start()
    {
        _monitor.Updated += OnUpdated;
        _timer = new System.Threading.Timer(_ => OnTimer(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        _ = CleanupAsync();
    }

    /// <summary>Штатное завершение: остановка и досрочный сброс накопленных записей (ТЗ 4.2.3).</summary>
    public void Stop()
    {
        _monitor.Updated -= OnUpdated;
        _timer?.Dispose();
        _timer = null;
        try { Task.Run(() => FlushAsync()).Wait(TimeSpan.FromSeconds(3)); }
        catch { /* при выходе ошибки записи уже некуда показывать */ }
    }

    private void OnUpdated(MetricsSnapshot s)
    {
        if (!_settings.SaveHistory || _monitor.Provider.IsDemo) return; // демо-данные в БД не пишем

        var now = DateTime.UtcNow;
        var period = Math.Max(1, _settings.HistoryIntervalSeconds);
        if ((now - _lastSavedUtc).TotalSeconds < period - 0.5) return;
        _lastSavedUtc = now;

        long ts = new DateTimeOffset(now).ToUnixTimeSeconds();
        Enqueue(ts, MetricKeys.Cpu, s.Cpu);
        Enqueue(ts, MetricKeys.Ram, s.RamPercent);
        Enqueue(ts, MetricKeys.Gpu, s.GpuLoad);
        Enqueue(ts, MetricKeys.CpuTemp, s.CpuTemp);
        Enqueue(ts, MetricKeys.GpuTemp, s.GpuTemp);
    }

    private void Enqueue(long ts, string type, double? value)
    {
        if (value is null || _queue.Count >= MaxQueue) return; // недоступные показатели не сохраняем
        _queue.Enqueue(new MetricRecord { Timestamp = ts, MetricType = type, Value = value.Value });
    }

    private void OnTimer()
    {
        _ = FlushAsync();
        if (Interlocked.Increment(ref _ticks) % 720 == 0) _ = CleanupAsync(); // раз в час
    }

    public async Task FlushAsync()
    {
        await _flushLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_queue.IsEmpty) return;

            var batch = new List<MetricRecord>();
            while (_queue.TryDequeue(out var r)) batch.Add(r);

            try
            {
                await _repo.AddRangeAsync(batch).ConfigureAwait(false);
                Report(null);
            }
            catch (Exception ex)
            {
                // возвращаем записи в очередь, чтобы повторить при следующем сбросе
                foreach (var r in batch)
                    if (_queue.Count < MaxQueue) _queue.Enqueue(r);
                Report(Loc.F("Hist_WriteErr", ex.Message));
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private async Task CleanupAsync()
    {
        try
        {
            var days = Math.Max(1, _settings.RetentionDays);
            await _repo.DeleteOlderThanAsync(DateTime.UtcNow.AddDays(-days)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Report(Loc.F("Hist_PurgeErr", ex.Message));
        }
    }

    private void Report(string? message)
    {
        if (message is null && !_hadError) return; // нечего сообщать
        _hadError = message is not null;
        StatusChanged?.Invoke(message);
    }
}
