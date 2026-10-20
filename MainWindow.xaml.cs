using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using PcMonitor.Services;
using PcMonitor.ViewModels;

namespace PcMonitor;

public partial class MainWindow : Window
{
    private readonly TrayService _tray = new();
    private readonly MainViewModel _vm;
    private WindowState _restoreState = WindowState.Normal;
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();
        _vm = (MainViewModel)DataContext;

        _tray.OpenRequested += ShowFromTray;
        _tray.SettingsRequested += () =>
        {
            _vm.ShowSettings();
            ShowFromTray();
        };
        _tray.ExitRequested += ExitApplication;

        _vm.Monitor.Updated += s => _tray.SetTooltip($"PC Monitor\nCPU {s.Cpu:0} % · RAM {s.RamPercent:0} %");
        _vm.Notifications.BalloonSink = (title, text) => _tray.ShowBalloon(title, text, warning: true);

        StateChanged += OnStateChanged;
        Closing += OnClosing;

        // Дескриптор окна нужен заранее: даже спрятанное в трее окно должно принимать просьбу «показать» от другого экземпляра
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        WindowActivation.AllowFromLowerIntegrity(hwnd);
        HwndSource.FromHwnd(hwnd)?.AddHook(OnWindowMessage);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == WindowActivation.ShowMessage)
        {
            ShowFromTray();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>Показывает окно из трея (или поднимает, если оно уже открыто).</summary>
    public void ShowFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = _restoreState;
        Activate();
        Topmost = true;   // приём, чтобы окно гарантированно оказалось поверх остальных
        Topmost = false;
        Focus();
    }

    /// <summary>Вызывается при завершении сеанса Windows: окно должно закрыться, а не уйти в трей.</summary>
    public void PrepareExit() => _exiting = true;

    private void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    private void HideToTray(bool showHint)
    {
        Hide();
        if (!showHint || _vm.Settings.TrayHintShown) return;

        _vm.Settings.TrayHintShown = true;
        SettingsService.Save(_vm.Settings);
        _tray.ShowBalloon("PC Monitor продолжает работать",
            "Приложение свёрнуто в трей. Дважды щёлкните по значку, чтобы открыть окно; «Выход» — в меню значка.",
            warning: false);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Окно без рамки при разворачивании выходит за экран на ширину рамки — компенсируем
        Root.Margin = WindowState == WindowState.Maximized
            ? SystemParameters.WindowResizeBorderThickness
            : new Thickness(0);

        if (WindowState != WindowState.Minimized)
            _restoreState = WindowState;
        else if (_vm.Settings.MinimizeToTray && !_exiting)
            HideToTray(showHint: true);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && _vm.Settings.CloseToTray)
        {
            e.Cancel = true;
            HideToTray(showHint: true);
            return;
        }

        _exiting = true;
        _tray.Dispose();
        _vm.Shutdown(); // сбросить накопленную историю и закрыть датчики
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
