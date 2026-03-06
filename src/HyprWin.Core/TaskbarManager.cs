using System.Text;
using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Manages hiding/showing the native Windows taskbar and reserving screen space
/// for the custom HyprWin top bar via AppBar registration.
/// Uses transparent layered-window approach instead of SW_HIDE so that
/// ToolbarWindow32 children remain alive for tray icon reading.
/// </summary>
public sealed class TaskbarManager : IDisposable
{
    private readonly List<IntPtr> _hiddenTaskbars = new();
    private readonly Dictionary<IntPtr, int> _savedExStyles = new();
    private bool _isHidden;
    private bool _disposed;

    /// <summary>
    /// Hide all native taskbars (primary and secondary monitors).
    /// Instead of SW_HIDE (which kills child window updates), we make
    /// the taskbar fully transparent + click-through via WS_EX_LAYERED.
    /// This keeps ToolbarWindow32 children alive so TrayIconService can read icons.
    /// </summary>
    public void HideTaskbar()
    {
        try
        {
            _hiddenTaskbars.Clear();
            _savedExStyles.Clear();

            // Find and hide all taskbar windows
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                var sb = new StringBuilder(256);
                NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
                string className = sb.ToString();

                if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                {
                    // Save original extended style for restoration
                    int originalExStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                    _savedExStyles[hwnd] = originalExStyle;

                    // Make the taskbar a layered window with alpha=0 (fully transparent)
                    // and WS_EX_TRANSPARENT (click-through) so it's invisible and non-interactive
                    // but its child windows (ToolbarWindow32) remain alive and populated.
                    int newExStyle = originalExStyle
                        | (int)NativeMethods.WS_EX_LAYERED
                        | (int)NativeMethods.WS_EX_TRANSPARENT;
                    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, newExStyle);
                    NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 0, NativeMethods.LWA_ALPHA);

                    _hiddenTaskbars.Add(hwnd);
                    Logger.Instance.Debug($"Hidden taskbar (transparent): {className} ({hwnd})");
                }
                return true;
            }, IntPtr.Zero);

            _isHidden = true;
            Logger.Instance.Info($"Hidden {_hiddenTaskbars.Count} taskbar(s) via transparency");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to hide taskbar", ex);
        }
    }

    /// <summary>
    /// Show (restore) all previously hidden taskbars.
    /// Removes the layered/transparent styles and restores the original extended style.
    /// </summary>
    public void ShowTaskbar()
    {
        try
        {
            foreach (var hwnd in _hiddenTaskbars)
            {
                if (_savedExStyles.TryGetValue(hwnd, out int originalExStyle))
                {
                    // Restore original extended style (removes LAYERED + TRANSPARENT)
                    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, originalExStyle);
                }
                else
                {
                    // Fallback: remove the styles we added
                    int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                    exStyle &= ~(int)(NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT);
                    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
                }

                // Force the taskbar to redraw and reclaim its AppBar space
                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);

                // Ensure the window is visible (in case a previous version used SW_HIDE)
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
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
                    // Remove any layered/transparent flags we might have set
                    int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                    if ((exStyle & (int)NativeMethods.WS_EX_LAYERED) != 0)
                    {
                        exStyle &= ~(int)(NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT);
                        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
                    }

                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
                }
                return true;
            }, IntPtr.Zero);

            // Use synchronous SendMessage so the shell processes WM_SETTINGCHANGE
            // before HyprWin exits — this ensures work-area reservations are refreshed.
            NativeMethods.SendMessage(NativeMethods.HWND_BROADCAST,
                NativeMethods.WM_SETTINGCHANGE, IntPtr.Zero, IntPtr.Zero);

            // Small delay then send again to catch any late-initializing shell components
            System.Threading.Thread.Sleep(100);
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST,
                NativeMethods.WM_SETTINGCHANGE, IntPtr.Zero, IntPtr.Zero);

            _hiddenTaskbars.Clear();
            _savedExStyles.Clear();
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
