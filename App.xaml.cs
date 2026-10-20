using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using PcMonitor.Services;

namespace PcMonitor;

public partial class App : Application
{
    private const string MutexName = @"Local\PcMonitor.SingleInstance";
    private const string SignalName = @"Local\PcMonitor.ShowWindow";

    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool autostart = e.Args.Contains(AutostartService.Argument, StringComparer.OrdinalIgnoreCase);

        // Второй экземпляр не запускаем: просим уже работающий (возможно, спрятанный в трее) показать окно
        if (!AcquireSingleInstance(autostart))
        {
            Shutdown();
            return;
        }

        // Числа и даты в разметке — по региональным настройкам пользователя
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        var settings = SettingsService.Load();
        ThemeService.Apply(settings.Theme); // до создания окна, чтобы не мигал стартовый вариант
        AutostartService.Refresh();
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        SessionEnding += (_, _) => window.PrepareExit(); // выход из Windows не должен блокироваться «сворачиванием в трей»

        // «Запускать свёрнутым» действует только при запуске системой, ручной запуск всегда показывает окно
        if (!(autostart && settings.StartInTray))
            window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _showSignal?.Dispose();
        base.OnExit(e);
    }

    private bool AcquireSingleInstance(bool autostart)
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);

            if (!createdNew)
            {
                if (!autostart) _showSignal.Set();
                return false;
            }

            var listener = new Thread(() =>
            {
                while (_showSignal.WaitOne())
                    Dispatcher.InvokeAsync(() => (MainWindow as MainWindow)?.ShowFromTray());
            })
            { IsBackground = true, Name = "PcMonitor.ShowSignal" };
            listener.Start();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Экземпляр уже работает с другими правами (например, запущен автозапуском от администратора),
            // и его объекты синхронизации недоступны. Второй экземпляр не нужен — просим показать окно.
            if (!autostart) WindowActivation.TryActivateRunningInstance();
            return false;
        }
        catch
        {
            return true; // не удалось создать объекты синхронизации по другой причине — не блокируем запуск
        }
    }
}
