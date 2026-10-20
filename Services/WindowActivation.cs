using System.Runtime.InteropServices;

namespace PcMonitor.Services;

/// <summary>
/// Просьба «покажи окно» между экземплярами с разными правами. Объекты синхронизации обычного
/// запуска недоступны процессу с повышенными правами (и наоборот: экземпляр администратора закрыт для обычного),
/// поэтому для этого случая используется оконное сообщение, которое явно разрешено пропускать через границу UIPI.
/// </summary>
public static class WindowActivation
{
    public const string WindowTitle = "PC Monitor";

    public static readonly uint ShowMessage = RegisterWindowMessage("PcMonitor.ShowWindow");

    /// <summary>Разрешает окну принимать наше сообщение от процессов с меньшими правами.</summary>
    public static void AllowFromLowerIntegrity(IntPtr hwnd)
    {
        try { ChangeWindowMessageFilterEx(hwnd, ShowMessage, MSGFLT_ALLOW, IntPtr.Zero); }
        catch { /* без этого дубли предотвращаются, но окно из другого экземпляра не покажется */ }
    }

    /// <summary>Просит уже работающий экземпляр показать окно. true — сообщение отправлено.</summary>
    public static bool TryActivateRunningInstance()
    {
        try
        {
            var hwnd = FindWindow(null, WindowTitle);
            return hwnd != IntPtr.Zero && PostMessage(hwnd, ShowMessage, IntPtr.Zero, IntPtr.Zero);
        }
        catch { return false; }
    }

    private const uint MSGFLT_ALLOW = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr pChangeFilterStruct);
}
