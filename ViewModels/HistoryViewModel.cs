using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcMonitor.Controls;
using PcMonitor.Data;
using PcMonitor.Models;
using PcMonitor.Services;

namespace PcMonitor.ViewModels;

public partial class PeriodOption : ObservableObject
{
    public required string Title { get; init; }
    public required TimeSpan Duration { get; init; }
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class HistoryViewModel : ObservableObject
{
    public const int BucketCount = 160;

    private readonly IHistoryRepository _repo;
    private int _version;          // отсекает устаревшие ответы при быстрой смене периода
    private PeriodOption _period = null!;   // текущий выбор запоминаем по событию, а не ищем по флагам:
    private SeriesOption _metric = null!;   // при клике на RadioButton несколько мгновений «выбраны» оба пункта
    private string _readError = "";
    private string _writeError = "";

    public IReadOnlyList<PeriodOption> Periods { get; }
    public IReadOnlyList<SeriesOption> Metrics { get; }

    [ObservableProperty] private IReadOnlyList<ChartSeries> _series = Array.Empty<ChartSeries>();
    [ObservableProperty] private IReadOnlyList<string> _xLabels = Array.Empty<string>();
    [ObservableProperty] private string _average = "—";
    [ObservableProperty] private string _maximum = "—";
    [ObservableProperty] private string _minimum = "—";
    [ObservableProperty] private string _records = "—";
    [ObservableProperty] private string _rangeText = "";
    [ObservableProperty] private string _dbInfo = "";
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] private string _errorText = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public HistoryViewModel(IHistoryRepository repo, HistoryRecorder recorder)
    {
        _repo = repo;

        Periods = new[]
        {
            new PeriodOption { Title = "15 минут", Duration = TimeSpan.FromMinutes(15) },
            new PeriodOption { Title = "1 час", Duration = TimeSpan.FromHours(1), IsSelected = true },
            new PeriodOption { Title = "6 часов", Duration = TimeSpan.FromHours(6) },
            new PeriodOption { Title = "24 часа", Duration = TimeSpan.FromHours(24) },
            new PeriodOption { Title = "7 дней", Duration = TimeSpan.FromDays(7) },
        };
        Metrics = new[]
        {
            new SeriesOption { Key = MetricKeys.Cpu, Name = "CPU", BrushKey = "CpuBrush", IsSelected = true },
            new SeriesOption { Key = MetricKeys.Ram, Name = "RAM", BrushKey = "RamBrush" },
            new SeriesOption { Key = MetricKeys.Gpu, Name = "GPU", BrushKey = "GpuBrush" },
            new SeriesOption { Key = MetricKeys.CpuTemp, Name = "Темп. CPU", BrushKey = "TempBrush", Unit = "°C" },
            new SeriesOption { Key = MetricKeys.GpuTemp, Name = "Темп. GPU", BrushKey = "DiskBrush", Unit = "°C" },
        };

        _period = Periods.First(p => p.IsSelected);
        _metric = Metrics.First(m => m.IsSelected);

        foreach (var p in Periods)
            p.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(PeriodOption.IsSelected) || !((PeriodOption)s!).IsSelected) return;
                _period = (PeriodOption)s;
                _ = RefreshAsync();
            };
        foreach (var m in Metrics)
            m.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(SeriesOption.IsSelected) || !((SeriesOption)s!).IsSelected) return;
                _metric = (SeriesOption)s;
                _ = RefreshAsync();
            };

        // ошибки записи приходят из фонового потока
        recorder.StatusChanged += msg =>
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                _writeError = msg ?? "";
                UpdateError();
            });
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        int version = ++_version;
        var period = _period;
        var metric = _metric;

        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc - period.Duration;
        var fromLocal = fromUtc.ToLocalTime();
        var toLocal = toUtc.ToLocalTime();

        string fmt = period.Duration.TotalHours <= 24 ? "HH:mm" : "dd.MM HH:mm";
        XLabels = Enumerable.Range(0, 5).Select(i => (fromLocal + period.Duration * i / 4).ToString(fmt)).ToList();
        RangeText = $"{fromLocal:dd.MM.yyyy HH:mm} — {toLocal:dd.MM.yyyy HH:mm}";

        try
        {
            var buckets = await _repo.GetSeriesAsync(metric.Key, fromUtc, toUtc, BucketCount);
            long total = await _repo.CountAsync();
            if (version != _version) return;

            // пустые интервалы — NaN: график покажет разрыв, а не выдуманную линию
            var values = Enumerable.Repeat(double.NaN, BucketCount).ToArray();
            foreach (var b in buckets)
                values[(int)Math.Clamp(b.Index, 0, BucketCount - 1)] = b.Average;

            Series = new[]
            {
                new ChartSeries { Name = metric.Name, BrushKey = metric.BrushKey, Unit = metric.Unit, Values = values },
            };
            HasData = buckets.Count > 0;

            if (HasData)
            {
                long count = buckets.Sum(b => (long)b.Count);
                double avg = buckets.Sum(b => b.Average * b.Count) / count;
                Average = $"{avg:0.#} {metric.Unit}";
                Maximum = $"{buckets.Max(b => b.Max):0.#} {metric.Unit}";
                Minimum = $"{buckets.Min(b => b.Min):0.#} {metric.Unit}";
                Records = count.ToString("N0");
            }
            else
            {
                Average = Maximum = Minimum = Records = "—";
            }

            DbInfo = $"В базе: {total:N0} записей";
            _readError = "";
        }
        catch (Exception ex)
        {
            if (version != _version) return;
            Series = Array.Empty<ChartSeries>();
            HasData = false;
            Average = Maximum = Minimum = Records = "—";
            _readError = $"Не удалось прочитать историю: {ex.Message}";
        }

        IsLoaded = true;
        UpdateError();
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        var answer = MessageBox.Show("Удалить всю сохранённую историю измерений? Это действие нельзя отменить.",
            "Очистка истории", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            await _repo.ClearAsync();
            _readError = "";
        }
        catch (Exception ex)
        {
            _readError = $"Не удалось очистить историю: {ex.Message}";
            UpdateError();
            return;
        }
        await RefreshAsync();
    }

    private void UpdateError() =>
        ErrorText = !string.IsNullOrEmpty(_readError) ? _readError : _writeError;
}
