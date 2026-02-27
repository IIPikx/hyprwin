using System.Text;
using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Manages hiding/showing the native Windows taskbar and reserving screen space
/// for the custom HyprWin top bar via AppBar registration.
/// </summary>
public sealed class TaskbarManager : IDisposable
{
    private readonly List<IntPtr> _hiddenTaskbars = new();
    private bool _isHidden;
    private bool _disposed;

    /// <summary>
    /// Hide all native taskbars (primary and secondary monitors).
    /// </summary>
    public void HideTaskbar()
    {
        try
        {
            _hiddenTaskbars.Clear();

            // Find and hide all taskbar windows
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                var sb = new StringBuilder(256);
                NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
                string className = sb.ToString();

                if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
                    _hiddenTaskbars.Add(hwnd);
                    Logger.Instance.Debug($"Hidden taskbar: {className} ({hwnd})");
                }
                return true;
            }, IntPtr.Zero);

            _isHidden = true;
            Logger.Instance.Info($"Hidden {_hiddenTaskbars.Count} taskbar(s)");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to hide taskbar", ex);
        }
    }

    /// <summary>
    /// Show (restore) all previously hidden taskbars.
    /// </summary>
    public void ShowTaskbar()
    {
        try
        {
            foreach (var hwnd in _hiddenTaskbars)
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                Logger.Instance.Debug($"Restored taskbar: {hwnd}");
            }

            // Also try to find any that may have been recreated
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                var sb = new StringBuilder(256);
                NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
                string className = sb.ToString();

                if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                }
                return true;
            }, IntPtr.Zero);

            _hiddenTaskbars.Clear();
            _isHidden = false;
            Logger.Instance.Info("Taskbar(s) restored");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to restore taskbar", ex);
        }
    }

    /// <summary>
    /// Re-hide taskbars (called when Explorer.exe restarts).
    /// </summary>
    public void ReHideIfNeeded()
    {
        if (_isHidden)
        {
            Logger.Instance.Info("Re-hiding taskbar (Explorer may have restarted)");
            HideTaskbar();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Always restore taskbar on disposal
        if (_isHidden)
        {
            ShowTaskbar();
        }
    }
}
