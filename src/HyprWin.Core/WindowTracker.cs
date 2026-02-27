using System.Runtime.InteropServices;
using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Represents a window being managed by HyprWin.
/// </summary>
public class ManagedWindow
{
    public IntPtr Handle { get; init; }
    public string Title { get; set; } = "";
    public string ClassName { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public NativeMethods.RECT Bounds { get; set; }
    public int WorkspaceId { get; set; }
    public int MonitorIndex { get; set; }
    public bool IsFloating { get; set; }
    public bool IsFullscreen { get; set; }
    public bool IsMinimized { get; set; }

    /// <summary>
    /// Saved bounds before going fullscreen or being tiled, for restoration.
    /// </summary>
    public NativeMethods.RECT? SavedBounds { get; set; }

    /// <summary>
    /// Original bounds captured when the window is first managed, for restore-on-exit.
    /// </summary>
    public NativeMethods.RECT OriginalBounds { get; init; }

    /// <summary>
    /// Original WINDOWPLACEMENT captured for proper restore (maximized state, etc.).
    /// </summary>
    public NativeMethods.WINDOWPLACEMENT OriginalPlacement { get; init; }

    public void RefreshBounds()
    {
        if (NativeMethods.GetWindowRect(Handle, out var rect))
            Bounds = rect;
    }

    public void RefreshTitle()
    {
        Title = NativeMethods.GetWindowTitle(Handle);
    }

    public override string ToString() => $"[{Handle}] {Title} WS={WorkspaceId} Mon={MonitorIndex}";
}

/// <summary>
/// Tracks all managed windows using Win32 event hooks.
/// Provides window discovery and lifecycle event forwarding.
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private readonly Dictionary<IntPtr, ManagedWindow> _windows = new();
    private readonly object _lock = new();
    private IntPtr _eventHookCreate;
    private IntPtr _eventHookForeground;
    private IntPtr _eventHookDestroy;
    private IntPtr _eventHookMoveSizeEnd;
    private IntPtr _eventHookMinimizeEnd;
    private NativeMethods.WinEventDelegate? _createCallback;
    private NativeMethods.WinEventDelegate? _foregroundCallback;
    private NativeMethods.WinEventDelegate? _destroyCallback;
    private NativeMethods.WinEventDelegate? _moveSizeEndCallback;
    private NativeMethods.WinEventDelegate? _minimizeEndCallback;
    private bool _disposed;

    private IntPtr _activeWindowHandle;

    // Exclusion lists
    private HashSet<string> _excludedProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _excludedClassNames = new(StringComparer.OrdinalIgnoreCase);

    // Our own process ID — never manage our own windows
    private readonly uint _ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

    public IntPtr ActiveWindowHandle => _activeWindowHandle;

    /// <summary>Fired when a new manageable window is detected.</summary>
    public event Action<ManagedWindow>? WindowAdded;

    /// <summary>Fired when a managed window is destroyed.</summary>
    public event Action<IntPtr>? WindowRemoved;

    /// <summary>Fired when the foreground (focused) window changes.</summary>
    public event Action<IntPtr>? FocusChanged;

    /// <summary>Fired when a managed window finishes being moved/resized by the user.</summary>
    public event Action<IntPtr>? WindowMoveSizeEnd;

    /// <summary>Fired when a window is restored from minimized state.</summary>
    public event Action<IntPtr>? WindowRestored;

    public IReadOnlyList<ManagedWindow> Windows
    {
        get { lock (_lock) return _windows.Values.ToList().AsReadOnly(); }
    }

    public ManagedWindow? GetWindow(IntPtr hwnd)
    {
        lock (_lock)
            return _windows.TryGetValue(hwnd, out var w) ? w : null;
    }

    public ManagedWindow? ActiveWindow
    {
        get
        {
            lock (_lock)
                return _windows.TryGetValue(_activeWindowHandle, out var w) ? w : null;
        }
    }

    /// <summary>
    /// Update the exclusion lists (process names and class names to ignore).
    /// </summary>
    public void SetExclusions(IEnumerable<string> processNames, IEnumerable<string> classNames)
    {
        _excludedProcessNames = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
        _excludedClassNames = new HashSet<string>(classNames, StringComparer.OrdinalIgnoreCase);
        Logger.Instance.Info($"Exclusions updated: {_excludedProcessNames.Count} processes, {_excludedClassNames.Count} classes");
    }

    /// <summary>
    /// Restore all managed windows to their original position and placement.
    /// Called during application shutdown.
    /// </summary>
    public void RestoreAllWindows()
    {
        List<ManagedWindow> windows;
        lock (_lock)
            windows = _windows.Values.ToList();

        int restored = 0;
        foreach (var w in windows)
        {
            try
            {
                if (!NativeMethods.IsWindow(w.Handle)) continue;

                // Restore original placement (handles maximized state, etc.)
                var placement = w.OriginalPlacement;
                if (placement.length > 0)
                {
                    NativeMethods.SetWindowPlacement(w.Handle, ref placement);
                    restored++;
                }
                else
                {
                    // Fallback: restore original bounds directly
                    var r = w.OriginalBounds;
                    if (r.Width > 0 && r.Height > 0)
                    {
                        NativeMethods.ShowWindow(w.Handle, NativeMethods.SW_RESTORE);
                        NativeMethods.SetWindowPos(w.Handle, IntPtr.Zero,
                            r.Left, r.Top, r.Width, r.Height,
                            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                        restored++;
                    }
                }

                // Make sure hidden windows are shown
                if (!NativeMethods.IsWindowVisible(w.Handle))
                    NativeMethods.ShowWindow(w.Handle, NativeMethods.SW_SHOW);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"Failed to restore window {w.Handle}: {ex.Message}");
            }
        }

        Logger.Instance.Info($"Restored {restored} window(s) to original positions");
    }

    /// <summary>
    /// Get the process name for a window handle.
    /// </summary>
    private static string GetProcessNameForWindow(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Start tracking windows: install event hooks and enumerate existing windows.
    /// Must be called from a thread with a message pump.
    /// </summary>
    public void Start(MonitorManager monitorManager)
    {
        // Keep delegate references alive to prevent GC
        _createCallback = OnWinEventCreate;
        _foregroundCallback = OnWinEventForeground;
        _destroyCallback = OnWinEventDestroy;
        _moveSizeEndCallback = OnWinEventMoveSizeEnd;
        _minimizeEndCallback = OnWinEventMinimizeEnd;

        _eventHookCreate = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_CREATE, NativeMethods.EVENT_OBJECT_SHOW,
            IntPtr.Zero, _createCallback,
            0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        _eventHookDestroy = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_DESTROY,
            IntPtr.Zero, _destroyCallback,
            0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        _eventHookForeground = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundCallback,
            0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        _eventHookMoveSizeEnd = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_MOVESIZEEND, NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, _moveSizeEndCallback,
            0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        // MinimizeEnd fires when a window is restored from minimized state.
        // We use this to trigger a retile so the window fills its BSP slot again.
        _eventHookMinimizeEnd = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_MINIMIZEEND, NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
            IntPtr.Zero, _minimizeEndCallback,
            0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        Logger.Instance.Info("Window event hooks installed");

        // Enumerate existing windows
        EnumerateExistingWindows(monitorManager);

        // Track current foreground
        _activeWindowHandle = NativeMethods.GetForegroundWindow();
    }

    /// <summary>
    /// Discover all currently visible, manageable windows.
    /// </summary>
    private void EnumerateExistingWindows(MonitorManager monitorManager)
    {
        var discovered = new List<ManagedWindow>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsManageableWindow(hwnd))
            {
                var mon = monitorManager.GetMonitorForWindow(hwnd);
                NativeMethods.GetWindowRect(hwnd, out var rect);

                var placement = new NativeMethods.WINDOWPLACEMENT { length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
                NativeMethods.GetWindowPlacement(hwnd, ref placement);

                var mw = new ManagedWindow
                {
                    Handle = hwnd,
                    Title = NativeMethods.GetWindowTitle(hwnd),
                    ClassName = NativeMethods.GetWindowClassName(hwnd),
                    ProcessName = GetProcessNameForWindow(hwnd),
                    Bounds = rect,
                    OriginalBounds = rect,
                    OriginalPlacement = placement,
                    MonitorIndex = mon?.Index ?? 0,
                    WorkspaceId = 0, // Default to workspace 0
                    IsMinimized = NativeMethods.IsIconic(hwnd),
                };

                lock (_lock)
                    _windows[hwnd] = mw;

                discovered.Add(mw);
            }
            return true;
        }, IntPtr.Zero);

        Logger.Instance.Info($"Discovered {discovered.Count} existing window(s)");
        foreach (var w in discovered)
            Logger.Instance.Debug($"  {w}");
    }

    /// <summary>
    /// Add a window to tracking manually (used during initial tiling).
    /// </summary>
    public void TrackWindow(ManagedWindow window)
    {
        lock (_lock)
            _windows[window.Handle] = window;
    }

    /// <summary>
    /// Remove a window from tracking.
    /// </summary>
    public bool UntrackWindow(IntPtr hwnd)
    {
        lock (_lock)
            return _windows.Remove(hwnd);
    }

    /// <summary>
    /// Determine if a window should be managed by HyprWin.
    /// Filters out tool windows, cloaked windows, invisible windows, excluded programs, etc.
    /// </summary>
    public bool IsManageableWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (!NativeMethods.IsWindowVisible(hwnd)) return false;
        if (!NativeMethods.IsWindow(hwnd)) return false;

        // Skip windows with no title
        int titleLen = NativeMethods.GetWindowTextLength(hwnd);
        if (titleLen == 0) return false;

        // Check window styles
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

        // Must not be a child window
        if ((style & (int)NativeMethods.WS_CHILD) != 0) return false;

        // Skip tool windows (unless they also have APPWINDOW)
        if ((exStyle & (int)NativeMethods.WS_EX_TOOLWINDOW) != 0 &&
            (exStyle & (int)NativeMethods.WS_EX_APPWINDOW) == 0)
            return false;

        // Skip NOACTIVATE windows
        if ((exStyle & (int)NativeMethods.WS_EX_NOACTIVATE) != 0) return false;

        // Skip cloaked windows (UWP/XAML Islands phantom windows)
        if (NativeMethods.IsWindowCloaked(hwnd)) return false;

        // Skip known system windows
        string className = NativeMethods.GetWindowClassName(hwnd);
        if (IsSystemWindow(className)) return false;

        // Skip our own process's windows (border, topbar, etc.)
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownPid) return false;

        // Skip excluded class names
        if (_excludedClassNames.Count > 0 && _excludedClassNames.Contains(className))
            return false;

        // Skip excluded process names
        if (_excludedProcessNames.Count > 0)
        {
            string procName = GetProcessNameForWindow(hwnd);
            if (_excludedProcessNames.Contains(procName))
                return false;
        }

        return true;
    }

    private static bool IsSystemWindow(string className)
    {
        return className switch
        {
            "Shell_TrayWnd" => true,
            "Shell_SecondaryTrayWnd" => true,
            "Progman" => true,
            "WorkerW" => true,
            "Windows.UI.Core.CoreWindow" => true,
            "ApplicationFrameInputSinkWindow" => true,
            _ => false,
        };
    }

    // ──────────────── WinEvent Callbacks ────────────────

    private void OnWinEventCreate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
            return;

        // Small delay to let the window fully initialize
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (!IsManageableWindow(hwnd)) return;

                lock (_lock)
                {
                    if (_windows.ContainsKey(hwnd)) return; // Already tracked
                }

                NativeMethods.GetWindowRect(hwnd, out var rect);
                var placement = new NativeMethods.WINDOWPLACEMENT { length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
                NativeMethods.GetWindowPlacement(hwnd, ref placement);

                var mw = new ManagedWindow
                {
                    Handle = hwnd,
                    Title = NativeMethods.GetWindowTitle(hwnd),
                    ClassName = NativeMethods.GetWindowClassName(hwnd),
                    ProcessName = GetProcessNameForWindow(hwnd),
                    Bounds = rect,
                    OriginalBounds = rect,
                    OriginalPlacement = placement,
                };

                lock (_lock)
                    _windows[hwnd] = mw;

                Logger.Instance.Debug($"Window created: {mw}");
                WindowAdded?.Invoke(mw);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("Error handling window create event", ex);
            }
        });
    }

    private void OnWinEventDestroy(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
            return;

        bool wasTracked;
        lock (_lock)
            wasTracked = _windows.Remove(hwnd);

        if (wasTracked)
        {
            Logger.Instance.Debug($"Window destroyed: {hwnd}");
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                WindowRemoved?.Invoke(hwnd);
            });
        }
    }

    private void OnWinEventForeground(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        _activeWindowHandle = hwnd;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            FocusChanged?.Invoke(hwnd);
        });
    }

    private void OnWinEventMoveSizeEnd(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        lock (_lock)
        {
            if (_windows.TryGetValue(hwnd, out var w))
                w.RefreshBounds();
        }

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            WindowMoveSizeEnd?.Invoke(hwnd);
        });
    }

    private void OnWinEventMinimizeEnd(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Update cached flag immediately
        lock (_lock)
        {
            if (_windows.TryGetValue(hwnd, out var w))
                w.IsMinimized = false;
        }

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            WindowRestored?.Invoke(hwnd);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_eventHookCreate != IntPtr.Zero) NativeMethods.UnhookWinEvent(_eventHookCreate);
        if (_eventHookDestroy != IntPtr.Zero) NativeMethods.UnhookWinEvent(_eventHookDestroy);
        if (_eventHookForeground != IntPtr.Zero) NativeMethods.UnhookWinEvent(_eventHookForeground);
        if (_eventHookMoveSizeEnd != IntPtr.Zero) NativeMethods.UnhookWinEvent(_eventHookMoveSizeEnd);
        if (_eventHookMinimizeEnd != IntPtr.Zero) NativeMethods.UnhookWinEvent(_eventHookMinimizeEnd);

        Logger.Instance.Info("Window event hooks removed");
    }
}
