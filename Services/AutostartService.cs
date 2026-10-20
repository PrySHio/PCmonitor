using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace PcMonitor.Services;

/// <summary>Текущий способ автозапуска: выключен / обычный / от имени администратора.</summary>
public readonly record struct AutostartState(bool Enabled, bool AsAdmin);

/// <summary>
/// Автозапуск двумя способами:
///  • обычный — запись в HKCU\...\Run (права администратора не нужны);
///  • от администратора — задача Планировщика Windows «с наивысшими правами» на вход в систему.
///    Только так Windows запускает программу с повышенными правами без окна UAC при каждом входе.
///    Создание и удаление такой задачи требует подтверждения UAC — один раз, при сохранении настройки.
/// Источник истины — система (реестр и Планировщик), а не settings.json: пользователь может отключить запуск
/// в «Диспетчере задач → Автозагрузка» или удалить задачу вручную.
/// Методы, запускающие процессы (GetState, Apply), нужно вызывать не из UI-потока.
/// </summary>
public static class AutostartService
{
    public const string Argument = "--autostart";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "PcMonitor";
    private const string TaskName = "PC Monitor Autostart";
    private const string NotBuiltMessage =
        "Автозапуск доступен только для собранного .exe (сейчас приложение запущено через dotnet).";

    /// <summary>Путь к .exe; null, если приложение запущено через «dotnet run» (регистрировать dotnet.exe нельзя).</summary>
    private static string? ExePath
    {
        get
        {
            var p = Environment.ProcessPath;
            return p is not null && !string.Equals(Path.GetFileNameWithoutExtension(p), "dotnet", StringComparison.OrdinalIgnoreCase)
                ? p : null;
        }
    }

    private static string? Command => ExePath is { } p ? $"\"{p}\" {Argument}" : null;

    /// <summary>Запущено ли приложение сейчас с правами администратора.</summary>
    public static bool IsProcessElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // ── Состояние ──────────────────────────────────────────────────────────
    /// <summary>Быстрая проверка обычного автозапуска (только реестр).</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (string.IsNullOrWhiteSpace(run?.GetValue(ValueName) as string)) return false;

            // Диспетчер задач помечает отключённые записи в StartupApproved (нечётный первый байт)
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            if (approved?.GetValue(ValueName) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1) return false;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Полное состояние автозапуска (запускает schtasks — вызывать из фонового потока).</summary>
    public static AutostartState GetState() =>
        TaskExists() ? new AutostartState(true, true) : new AutostartState(IsEnabled(), false);

    // ── Изменение ──────────────────────────────────────────────────────────
    /// <summary>
    /// Приводит автозапуск к нужному состоянию. Возвращает текст ошибки или null при успехе.
    /// Для режима «от администратора» (включение и выключение) Windows покажет запрос UAC.
    /// </summary>
    public static string? Apply(bool enable, bool asAdmin)
    {
        try
        {
            if (enable && asAdmin)
            {
                var exe = ExePath;
                if (exe is null) return NotBuiltMessage;

                var error = CreateTask(exe);
                if (error is not null) return error;
                return SetRegistry(false); // иначе приложение запустилось бы дважды: из реестра и из Планировщика
            }

            if (TaskExists())
            {
                var error = DeleteTask();
                if (error is not null) return error;
            }
            return SetRegistry(enable);
        }
        catch (Exception ex)
        {
            return $"Не удалось изменить автозапуск: {ex.Message}";
        }
    }

    /// <summary>Если приложение перенесли в другую папку — обновляет путь в записи автозапуска (реестр).</summary>
    public static void Refresh()
    {
        try
        {
            var cmd = Command;
            if (cmd is null) return;
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run?.GetValue(ValueName) is string current && current != cmd)
                run.SetValue(ValueName, cmd);
        }
        catch { }
    }

    private static string? SetRegistry(bool enable)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable)
            {
                var cmd = Command;
                if (cmd is null) return NotBuiltMessage;

                run.SetValue(ValueName, cmd);
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                approved?.DeleteValue(ValueName, throwOnMissingValue: false); // снять отметку «отключено»
            }
            else
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return null;
        }
        catch (Exception ex)
        {
            return $"Не удалось изменить автозапуск: {ex.Message}";
        }
    }

    // ── Планировщик задач ──────────────────────────────────────────────────
    private static bool TaskExists()
    {
        try
        {
            var (code, _) = RunSchtasks($"/Query /TN \"{TaskName}\"", elevate: false);
            return code == 0;
        }
        catch { return false; }
    }

    private static string? CreateTask(string exe)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"pcmonitor-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(exe), Encoding.Unicode); // Планировщик ожидает UTF-16
            var (code, output) = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F", elevate: !IsProcessElevated());
            if (code == 0 && TaskExists()) return null;
            return code == -2
                ? "Запрос Windows (UAC) отклонён — автозапуск от администратора не включён."
                : $"Не удалось создать задачу автозапуска (код {code}). {output}".Trim();
        }
        catch (Exception ex)
        {
            return $"Не удалось создать задачу автозапуска: {ex.Message}";
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
    }

    private static string? DeleteTask()
    {
        try
        {
            var (code, output) = RunSchtasks($"/Delete /TN \"{TaskName}\" /F", elevate: !IsProcessElevated());
            if (code == 0 || !TaskExists()) return null;
            return code == -2
                ? "Запрос Windows (UAC) отклонён — режим автозапуска не изменён."
                : $"Не удалось удалить задачу автозапуска (код {code}). {output}".Trim();
        }
        catch (Exception ex)
        {
            return $"Не удалось удалить задачу автозапуска: {ex.Message}";
        }
    }

    /// <summary>Описание задачи: при входе текущего пользователя, с наивысшими правами, без ограничений по питанию и времени.</summary>
    private static string BuildTaskXml(string exe)
    {
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name) ?? "";
        string cmd = SecurityElement.Escape(exe) ?? "";
        string dir = SecurityElement.Escape(Path.GetDirectoryName(exe) ?? "") ?? "";

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
        sb.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
        sb.AppendLine("  <RegistrationInfo><Description>Автозапуск PC Monitor с правами администратора</Description></RegistrationInfo>");
        sb.AppendLine("  <Triggers>");
        sb.AppendLine($"    <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT5S</Delay></LogonTrigger>");
        sb.AppendLine("  </Triggers>");
        sb.AppendLine("  <Principals>");
        sb.AppendLine($"    <Principal id=\"Author\"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>");
        sb.AppendLine("  </Principals>");
        sb.AppendLine("  <Settings>");
        sb.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
        sb.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
        sb.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
        sb.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
        sb.AppendLine("    <StartWhenAvailable>false</StartWhenAvailable>");
        sb.AppendLine("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
        sb.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
        sb.AppendLine("    <Enabled>true</Enabled>");
        sb.AppendLine("    <Hidden>false</Hidden>");
        sb.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
        sb.AppendLine("    <Priority>7</Priority>");
        sb.AppendLine("  </Settings>");
        sb.AppendLine("  <Actions Context=\"Author\">");
        sb.AppendLine($"    <Exec><Command>{cmd}</Command><Arguments>{Argument}</Arguments><WorkingDirectory>{dir}</WorkingDirectory></Exec>");
        sb.AppendLine("  </Actions>");
        sb.AppendLine("</Task>");
        return sb.ToString();
    }

    /// <summary>
    /// Запускает schtasks. При elevate=true Windows показывает запрос UAC.
    /// Код -2 — пользователь отклонил запрос, -1 — не дождались завершения.
    /// </summary>
    private static (int code, string output) RunSchtasks(string args, bool elevate)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args) { CreateNoWindow = true };
        if (elevate)
        {
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Hidden;
        }
        else
        {
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
        }

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (-1, "");

            string output = "";
            if (!elevate) output = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
            if (!p.WaitForExit(60_000)) return (-1, "Время ожидания истекло.");
            return (p.ExitCode, output);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: отказ в окне UAC
        {
            return (-2, "");
        }
    }
}
