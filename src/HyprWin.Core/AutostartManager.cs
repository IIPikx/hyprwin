using System.Diagnostics;
using Microsoft.Win32;

namespace HyprWin.Core;

/// <summary>
/// Manages HyprWin autostart.
/// Since HyprWin runs elevated (requireAdministrator), Windows silently ignores standard
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run entries at user logon.
/// Autostart is therefore registered via Windows Task Scheduler with HighestAvailable privileges (schtasks.exe),
/// with the Run registry key maintained as a secondary fallback.
/// </summary>
public static class AutostartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "HyprWin";
    private const string TaskName = "HyprWin";

    /// <summary>
    /// Returns true if HyprWin is registered to start with Windows (either Task Scheduler or Registry).
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            if (IsTaskEnabled())
                return true;

            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enable autostart: registers an elevated logon task via Task Scheduler and sets the Run registry entry.
    /// </summary>
    public static void Enable()
    {
        try
        {
            var exePath = GetExePath();
            if (exePath == null)
            {
                Logger.Instance.Warn("Cannot enable autostart: executable path unknown");
                return;
            }

            // 1. Primary: Register elevated task in Task Scheduler (starts elevated without UAC prompts at logon)
            bool taskCreated = CreateTask(exePath);
            if (taskCreated)
            {
                Logger.Instance.Info($"Autostart scheduled task created successfully for: {exePath}");
            }
            else
            {
                Logger.Instance.Warn("Could not create scheduled task, falling back to registry Run key only");
            }

            // 2. Secondary: Set HKCU Run key as fallback
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key != null)
            {
                key.SetValue(AppName, $"\"{exePath}\"");
                Logger.Instance.Info($"Autostart registry entry set: {exePath}");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to enable autostart", ex);
        }
    }

    /// <summary>
    /// Disable autostart: removes the scheduled task and the Run registry entry.
    /// </summary>
    public static void Disable()
    {
        try
        {
            // 1. Delete scheduled task
            DeleteTask();

            // 2. Delete registry entry
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(AppName) != null)
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
                Logger.Instance.Info("Autostart registry entry removed");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to disable autostart", ex);
        }
    }

    /// <summary>
    /// Set autostart state based on config value.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        if (enabled)
            Enable();
        else
            Disable();
    }

    private static bool IsTaskEnabled()
    {
        return RunSchtasks($"/query /tn \"{TaskName}\"");
    }

    private static bool CreateTask(string exePath)
    {
        string args = $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\"\" /sc ONLOGON /rl HIGHEST /f";
        return RunSchtasks(args);
    }

    private static bool DeleteTask()
    {
        return RunSchtasks($"/delete /tn \"{TaskName}\" /f");
    }

    private static bool RunSchtasks(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(4000);
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.Instance.Debug($"schtasks execution ({arguments}): {ex.Message}");
            return false;
        }
    }

    private static string? GetExePath()
    {
        return Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName;
    }
}
