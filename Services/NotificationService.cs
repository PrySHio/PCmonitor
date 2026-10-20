using System.Collections.ObjectModel;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;

namespace PcMonitor.Services;

public sealed class ToastItem
{
    public ToastItem(string title, string message)
    {
        Title = title;
        Message = message;
        Time = DateTime.Now.ToString("HH:mm:ss");
    }

    public string Title { get; }
    public string Message { get; }
    public string Time { get; }
    public ICommand? DismissCommand { get; set; }
}

/// <summary>
/// Показывает уведомления поверх окна (самоскрывающиеся «тосты»), при необходимости со звуком,
/// а если окно не в фокусе или свёрнуто — мигает кнопкой на панели задач. Вызывать из UI-потока.
/// </summary>
public sealed class NotificationService
{
    private const int MaxToasts = 4;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    public ObservableCollection<ToastItem> Toasts { get; } = new();

    /// <summary>Показ системного сообщения у значка в трее — используется, когда окно спрятано и тост не виден.</summary>
    public Action<string, string>? BalloonSink { get; set; }

    public void Show(string title, string message, bool sound)
    {
        var window = Application.Current?.MainWindow;
        if (window is not null && !window.IsVisible && BalloonSink is not null)
        {
            BalloonSink(title, message); // окно в трее: сообщение у значка (звук проигрывает сама система)
            return;
        }

        var toast = new ToastItem(title, message);
        toast.DismissCommand = new RelayCommand(() => Toasts.Remove(toast));

        Toasts.Add(toast);
        while (Toasts.Count > MaxToasts) Toasts.RemoveAt(0);

        var timer = new DispatcherTimer { Interval = Lifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Toasts.Remove(toast);
        };
        timer.Start();

        if (sound)
        {
            try { SystemSounds.Exclamation.Play(); } catch { /* звук не критичен */ }
        }
        FlashTaskbarIfInactive();
    }

    private static void FlashTaskbarIfInactive()
    {
        try
        {
            var window = Application.Current?.MainWindow;
            if (window is null || window.IsActive) return;

            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = handle,
                dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG, // мигать, пока окно не станет активным
                uCount = uint.MaxValue,
            };
            FlashWindowEx(ref info);
        }
        catch { /* мигание — необязательное улучшение */ }
    }

    private const uint FLASHW_ALL = 3;
    private const uint FLASHW_TIMERNOFG = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
}
