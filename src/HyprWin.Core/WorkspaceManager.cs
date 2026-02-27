using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Represents a single virtual workspace on a monitor.
/// </summary>
public class Workspace
{
    public int Id { get; init; }
    public int MonitorIndex { get; init; }
    public List<ManagedWindow> Windows { get; } = new();
    public ManagedWindow? FocusedWindow { get; set; }

    /// <summary>The BSP tree for this workspace's tiling layout.</summary>
    public BspNode? LayoutRoot { get; set; }
}

/// <summary>
/// Manages virtual workspaces per monitor. Each monitor has N independent workspaces.
/// Handles workspace switching, window assignment, and cross-workspace/monitor moves.
/// </summary>
public sealed class WorkspaceManager
{
    private readonly MonitorManager _monitorManager;
    private readonly WindowTracker _windowTracker;
    private readonly Dictionary<int, Workspace[]> _monitorWorkspaces = new(); // monitorIndex -> Workspace[]
    private readonly Dictionary<int, int> _activeWorkspaceIndex = new(); // monitorIndex -> active ws index
    private int _workspaceCount;

    /// <summary>Fired when the active workspace changes on any monitor.</summary>
    public event Action<int, int>? WorkspaceSwitched; // monitorIndex, newWorkspaceId

    /// <summary>Fired when windows need to be retiled on a workspace.</summary>
    public event Action<Workspace>? RetileRequested;

    public WorkspaceManager(MonitorManager monitorManager, WindowTracker windowTracker)
    {
        _monitorManager = monitorManager;
        _windowTracker = windowTracker;
    }

    /// <summary>
    /// Initialize workspaces for all monitors.
    /// </summary>
    public void Initialize(int workspaceCount = 3)
    {
        _workspaceCount = workspaceCount;
        _monitorWorkspaces.Clear();
        _activeWorkspaceIndex.Clear();

        foreach (var mon in _monitorManager.Monitors)
        {
            var workspaces = new Workspace[workspaceCount];
            for (int i = 0; i < workspaceCount; i++)
            {
                workspaces[i] = new Workspace
                {
                    Id = i,
                    MonitorIndex = mon.Index,
                };
            }
            _monitorWorkspaces[mon.Index] = workspaces;
            _activeWorkspaceIndex[mon.Index] = 0;
        }

        Logger.Instance.Info($"Initialized {workspaceCount} workspaces per monitor ({_monitorManager.Count} monitors)");
    }

    /// <summary>
    /// Assign existing tracked windows to workspace 0 of their respective monitors.
    /// </summary>
    public void AssignExistingWindows()
    {
        foreach (var w in _windowTracker.Windows)
        {
            var mon = _monitorManager.GetMonitorForWindow(w.Handle);
            int monIdx = mon?.Index ?? 0;
            w.MonitorIndex = monIdx;
            w.WorkspaceId = 0;

            var ws = GetWorkspace(monIdx, 0);
            if (ws != null && !ws.Windows.Contains(w))
            {
                ws.Windows.Add(w);
            }
        }

        Logger.Instance.Info("Assigned existing windows to workspaces");
    }

    /// <summary>
    /// Get the workspace object for a given monitor and workspace index.
    /// </summary>
    public Workspace? GetWorkspace(int monitorIndex, int workspaceIndex)
    {
        if (_monitorWorkspaces.TryGetValue(monitorIndex, out var workspaces) &&
            workspaceIndex >= 0 && workspaceIndex < workspaces.Length)
            return workspaces[workspaceIndex];
        return null;
    }

    /// <summary>
    /// Get the active workspace for a given monitor.
    /// </summary>
    public Workspace? GetActiveWorkspace(int monitorIndex)
    {
        if (_activeWorkspaceIndex.TryGetValue(monitorIndex, out var wsIndex))
            return GetWorkspace(monitorIndex, wsIndex);
        return null;
    }

    /// <summary>
    /// Get the active workspace index for a monitor.
    /// </summary>
    public int GetActiveWorkspaceIndex(int monitorIndex)
    {
        return _activeWorkspaceIndex.TryGetValue(monitorIndex, out var idx) ? idx : 0;
    }

    /// <summary>
    /// Switch to a different workspace on a monitor (virtual mode).
    /// Moves old workspace windows off-screen and restores new workspace windows.
    /// Does NOT use SW_HIDE to avoid apps misbehaving/closing.
    /// </summary>
    public void SwitchWorkspace(int monitorIndex, int workspaceIndex)
    {
        if (workspaceIndex < 0 || workspaceIndex >= _workspaceCount) return;
        if (!_activeWorkspaceIndex.TryGetValue(monitorIndex, out var currentIdx)) return;
        if (currentIdx == workspaceIndex) return; // Already on this workspace

        var oldWs = GetWorkspace(monitorIndex, currentIdx);
        var newWs = GetWorkspace(monitorIndex, workspaceIndex);
        if (oldWs == null || newWs == null) return;

        // Move old workspace windows off-screen (preserves window state, no close risk)
        foreach (var w in oldWs.Windows)
        {
            if (!w.IsMinimized && NativeMethods.IsWindow(w.Handle))
            {
                // Save current bounds before moving off-screen
                NativeMethods.GetWindowRect(w.Handle, out var rect);
                w.Bounds = rect;
                NativeMethods.SetWindowPos(w.Handle, IntPtr.Zero,
                    -32000, -32000, rect.Width, rect.Height,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSENDCHANGING);
            }
        }

        _activeWorkspaceIndex[monitorIndex] = workspaceIndex;

        // Retile the new workspace (this positions windows correctly on-screen)
        RetileRequested?.Invoke(newWs);

        // Focus the last focused window on the new workspace
        if (newWs.FocusedWindow != null && NativeMethods.IsWindow(newWs.FocusedWindow.Handle))
        {
            NativeMethods.ForceForegroundWindow(newWs.FocusedWindow.Handle);
        }
        else if (newWs.Windows.Count > 0)
        {
            NativeMethods.ForceForegroundWindow(newWs.Windows[0].Handle);
        }

        Logger.Instance.Info($"Switched monitor {monitorIndex} to workspace {workspaceIndex}");
        WorkspaceSwitched?.Invoke(monitorIndex, workspaceIndex);
    }

    /// <summary>
    /// Add a window to the active workspace of its monitor.
    /// </summary>
    public void AddWindowToActiveWorkspace(ManagedWindow window)
    {
        var mon = _monitorManager.GetMonitorForWindow(window.Handle);
        int monIdx = mon?.Index ?? 0;
        int wsIdx = GetActiveWorkspaceIndex(monIdx);

        window.MonitorIndex = monIdx;
        window.WorkspaceId = wsIdx;

        var ws = GetWorkspace(monIdx, wsIdx);
        if (ws != null && !ws.Windows.Contains(window))
        {
            ws.Windows.Add(window);
            ws.FocusedWindow = window;
            RetileRequested?.Invoke(ws);
        }
    }

    /// <summary>
    /// Remove a window from its workspace.
    /// </summary>
    public void RemoveWindow(IntPtr hwnd)
    {
        foreach (var (monIdx, workspaces) in _monitorWorkspaces)
        {
            foreach (var ws in workspaces)
            {
                var w = ws.Windows.FirstOrDefault(w => w.Handle == hwnd);
                if (w != null)
                {
                    ws.Windows.Remove(w);
                    if (ws.FocusedWindow?.Handle == hwnd)
                        ws.FocusedWindow = ws.Windows.LastOrDefault();

                    // Only retile if this is the active workspace
                    if (GetActiveWorkspaceIndex(monIdx) == ws.Id)
                        RetileRequested?.Invoke(ws);

                    return;
                }
            }
        }
    }

    /// <summary>
    /// Move a window to a different workspace.
    /// </summary>
    public void MoveWindowToWorkspace(IntPtr hwnd, int targetWorkspaceIndex)
    {
        if (targetWorkspaceIndex < 0 || targetWorkspaceIndex >= _workspaceCount) return;

        ManagedWindow? window = null;
        Workspace? sourceWs = null;
        int sourceMonitor = 0;

        // Find the window
        foreach (var (monIdx, workspaces) in _monitorWorkspaces)
        {
            foreach (var ws in workspaces)
            {
                var w = ws.Windows.FirstOrDefault(w => w.Handle == hwnd);
                if (w != null)
                {
                    window = w;
                    sourceWs = ws;
                    sourceMonitor = monIdx;
                    break;
                }
            }
            if (window != null) break;
        }

        if (window == null || sourceWs == null) return;

        var targetWs = GetWorkspace(sourceMonitor, targetWorkspaceIndex);
        if (targetWs == null || targetWs == sourceWs) return;

        // Move window
        sourceWs.Windows.Remove(window);
        if (sourceWs.FocusedWindow?.Handle == hwnd)
            sourceWs.FocusedWindow = sourceWs.Windows.LastOrDefault();

        window.WorkspaceId = targetWorkspaceIndex;
        targetWs.Windows.Add(window);

        // If moving to a non-active workspace, hide the window
        if (GetActiveWorkspaceIndex(sourceMonitor) != targetWorkspaceIndex)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
        }

        // Retile both workspaces if they're active
        if (GetActiveWorkspaceIndex(sourceMonitor) == sourceWs.Id)
            RetileRequested?.Invoke(sourceWs);
        if (GetActiveWorkspaceIndex(sourceMonitor) == targetWs.Id)
            RetileRequested?.Invoke(targetWs);

        Logger.Instance.Info($"Moved window {hwnd} to workspace {targetWorkspaceIndex}");
    }

    /// <summary>
    /// Move a window to a different monitor's active workspace.
    /// </summary>
    public void MoveWindowToMonitor(IntPtr hwnd, int targetMonitorIndex, bool fireRetile = true)
    {
        var targetMon = _monitorManager.GetByIndex(targetMonitorIndex);
        if (targetMon == null) return;

        ManagedWindow? window = null;
        Workspace? sourceWs = null;

        // Find the window
        foreach (var (monIdx, workspaces) in _monitorWorkspaces)
        {
            foreach (var ws in workspaces)
            {
                var w = ws.Windows.FirstOrDefault(w => w.Handle == hwnd);
                if (w != null)
                {
                    window = w;
                    sourceWs = ws;
                    break;
                }
            }
            if (window != null) break;
        }

        if (window == null || sourceWs == null) return;
        if (window.MonitorIndex == targetMonitorIndex) return;

        int targetWsIdx = GetActiveWorkspaceIndex(targetMonitorIndex);
        var targetWs = GetWorkspace(targetMonitorIndex, targetWsIdx);
        if (targetWs == null) return;

        // Remove from source
        sourceWs.Windows.Remove(window);
        if (sourceWs.FocusedWindow?.Handle == hwnd)
            sourceWs.FocusedWindow = sourceWs.Windows.LastOrDefault();

        // Add to target
        window.MonitorIndex = targetMonitorIndex;
        window.WorkspaceId = targetWsIdx;
        targetWs.Windows.Add(window);
        targetWs.FocusedWindow = window;

        // Retile both
        int sourceMonIdx = sourceWs.MonitorIndex;
        if (fireRetile)
        {
            if (GetActiveWorkspaceIndex(sourceMonIdx) == sourceWs.Id)
                RetileRequested?.Invoke(sourceWs);
            RetileRequested?.Invoke(targetWs);
        }

        Logger.Instance.Info($"Moved window {hwnd} to monitor {targetMonitorIndex}");
    }

    /// <summary>
    /// Update the focused window for the active workspace of the appropriate monitor.
    /// </summary>
    public void UpdateFocus(IntPtr hwnd)
    {
        foreach (var (monIdx, workspaces) in _monitorWorkspaces)
        {
            var activeWs = GetActiveWorkspace(monIdx);
            if (activeWs == null) continue;

            var w = activeWs.Windows.FirstOrDefault(w => w.Handle == hwnd);
            if (w != null)
            {
                activeWs.FocusedWindow = w;
                return;
            }
        }
    }

    /// <summary>
    /// Get the monitor index that the focused/active window is on.
    /// </summary>
    public int GetFocusedMonitorIndex()
    {
        var fgHwnd = NativeMethods.GetForegroundWindow();
        var mon = _monitorManager.GetMonitorForWindow(fgHwnd);
        return mon?.Index ?? 0;
    }
}
