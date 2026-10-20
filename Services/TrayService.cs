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
    private readonly WinForms.ToolStripMenuItem _openItem;
    private readonly WinForms.ToolStripMenuItem _settingsItem;
    private readonly WinForms.ToolStripMenuItem _exitItem;
    private string _tooltip = "";

    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        _appIcon = LoadIcon();

        _openItem = new WinForms.ToolStripMenuItem();
        _openItem.Font = new Drawing.Font(_openItem.Font, Drawing.FontStyle.Bold); // пункт по умолчанию
        _openItem.Click += (_, _) => OpenRequested?.Invoke();

        _settingsItem = new WinForms.ToolStripMenuItem();
        _settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        _exitItem = new WinForms.ToolStripMenuItem();
        _exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.AddRange(new WinForms.ToolStripItem[] { _openItem, _settingsItem, new WinForms.ToolStripSeparator(), _exitItem });
        UpdateTexts();

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

    /// <summary>Подписи пунктов меню на текущем языке (вызывается при старте и при смене языка).</summary>
    public void UpdateTexts()
    {
        _openItem.Text = Loc.T("Tray_Open");
        _settingsItem.Text = Loc.T("Nav_Settings");
        _exitItem.Text = Loc.T("Tray_Exit");
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
