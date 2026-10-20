using System.Windows;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace PcMonitor.Services;

/// <summary>
/// Значок в области уведомлений: меню «Открыть / Настройки / Выход», двойной щелчок — открыть окно,
/// подсказка с текущей загрузкой и всплывающие сообщения. Использует NotifyIcon из WinForms —
/// в WPF это стандартный способ; все вызовы выполняются в UI-потоке.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly Drawing.Icon _appIcon;
    private string _tooltip = "";

    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        _appIcon = LoadIcon();

        var open = new WinForms.ToolStripMenuItem("Открыть");
        open.Font = new Drawing.Font(open.Font, Drawing.FontStyle.Bold); // пункт по умолчанию
        open.Click += (_, _) => OpenRequested?.Invoke();

        var settings = new WinForms.ToolStripMenuItem("Настройки");
        settings.Click += (_, _) => SettingsRequested?.Invoke();

        var exit = new WinForms.ToolStripMenuItem("Выход");
        exit.Click += (_, _) => ExitRequested?.Invoke();

        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.AddRange(new WinForms.ToolStripItem[] { open, settings, new WinForms.ToolStripSeparator(), exit });

        _icon = new WinForms.NotifyIcon
        {
            Icon = _appIcon,
            Text = "PC Monitor",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke();
        };
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
    }

    /// <summary>Текст подсказки при наведении. Обновляется только при изменении.</summary>
    public void SetTooltip(string text)
    {
        if (text == _tooltip) return;
        _tooltip = text;
        _icon.Text = text.Length > 120 ? text[..120] : text; // лимит подсказки NotifyIcon — 127 символов
    }

    public void ShowBalloon(string title, string text, bool warning)
    {
        try
        {
            _icon.ShowBalloonTip(6000, title, text, warning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);
        }
        catch { /* сообщение необязательно */ }
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (info is not null)
            {
                using var stream = info.Stream;
                return new Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
            }
        }
        catch { }
        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        _icon.Visible = false; // иначе значок «зависает» в трее до наведения мыши
        _icon.Dispose();
        _menu.Dispose();
        _appIcon.Dispose();
    }
}
