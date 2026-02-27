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
                // Force the taskbar to reclaim its AppBar space
                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
                Logger.Instance.Debug($"Restored taskbar: {hwnd}");
            }

            // Also catch any taskbar that may have been recreated or missed
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                var sb = new StringBuilder(256);
                NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
                string className = sb.ToString();

                if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
                }
                return true;
            }, IntPtr.Zero);

            // Broadcast WM_SETTINGCHANGE so the shell refreshes all work-area reservations
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST,
                NativeMethods.WM_SETTINGCHANGE, IntPtr.Zero, IntPtr.Zero);

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

        // Always attempt to show the taskbar on disposal — even if _isHidden is false
        // (it may be false due to a failed HideTaskbar call at startup)
        ShowTaskbar();
    }
}
