using Microsoft.Win32;

namespace HyprWin.Core;

/// <summary>
/// Manages HyprWin autostart via the Windows registry (HKCU\…\Run).
/// Uses the current executable path so it works for both installed and portable deployments.
/// </summary>
public static class AutostartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "HyprWin";

    /// <summary>
    /// Returns true if HyprWin is registered to start with Windows.
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enable autostart: write the current exe path into HKCU\…\Run.
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

            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null)
            {
                Logger.Instance.Warn("Cannot enable autostart: Run key not found");
                return;
            }

            key.SetValue(AppName, $"\"{exePath}\"");
            Logger.Instance.Info($"Autostart enabled: {exePath}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to enable autostart", ex);
        }
    }

    /// <summary>
    /// Disable autostart: remove the Run entry.
    /// </summary>
    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(AppName) != null)
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
                Logger.Instance.Info("Autostart disabled");
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

    private static string? GetExePath()
    {
        return Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
    }
}
