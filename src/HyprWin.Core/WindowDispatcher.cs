using System.Diagnostics;
using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Dispatches window management actions (focus, move, close, toggle float, fullscreen, etc.)
/// in response to keybind triggers.
/// </summary>
public sealed class WindowDispatcher
{
    private readonly WindowTracker _windowTracker;
    private readonly WorkspaceManager _workspaceManager;
    private readonly TilingEngine _tilingEngine;
    private readonly MonitorManager _monitorManager;
    private string _terminalCommand = "wt.exe";
    private string _workspaceMode = "monitor_bound"; // "monitor_bound" or "virtual"

    public WindowDispatcher(
        WindowTracker windowTracker,
        WorkspaceManager workspaceManager,
        TilingEngine tilingEngine,
        MonitorManager monitorManager)
    {
        _windowTracker = windowTracker;
        _workspaceManager = workspaceManager;
        _tilingEngine = tilingEngine;
        _monitorManager = monitorManager;
    }

    public void SetTerminalCommand(string command) => _terminalCommand = command;
    public void SetWorkspaceMode(string mode) => _workspaceMode = mode;

    // ──────────────── Focus Navigation ────────────────

    public void FocusLeft() => FocusInDirection(-1, 0);
    public void FocusRight() => FocusInDirection(1, 0);
    public void FocusUp() => FocusInDirection(0, -1);
    public void FocusDown() => FocusInDirection(0, 1);

    /// <summary>
    /// Move focus to the nearest window in the given direction using spatial heuristics.
    /// If no window exists in that direction, focus stays on the current window (no cycling).
    /// </summary>
    private void FocusInDirection(int dx, int dy)
    {
        try
        {
            var fgHwnd = NativeMethods.GetForegroundWindow();
            Logger.Instance.Debug($"FocusInDirection({dx},{dy}) — foreground={fgHwnd}");

            int monIdx = _workspaceManager.GetFocusedMonitorIndex();

            // Collect visible windows from ALL active workspaces across every monitor.
            // This enables seamless cross-monitor directional focus.
            var allWindows = _monitorManager.Monitors
                .SelectMany(mon =>
                    _workspaceManager.GetActiveWorkspace(mon.Index)?.Windows
                    ?? Enumerable.Empty<ManagedWindow>())
                .Where(w => !w.IsMinimized && NativeMethods.IsWindow(w.Handle) && NativeMethods.IsWindowVisible(w.Handle))
                .ToList();

            Logger.Instance.Debug($"FocusInDirection({dx},{dy}): {allWindows.Count} visible windows across all monitors");

            if (allWindows.Count == 0) return;
            if (allWindows.Count == 1)
            {
                // Only one window — just focus it
                NativeMethods.ForceForegroundWindow(allWindows[0].Handle);
                _workspaceManager.UpdateFocus(allWindows[0].Handle);
                return;
            }

            // Spatial direction search — find the closest window in the given direction
            ManagedWindow? best = null;

            if (NativeMethods.GetWindowRect(fgHwnd, out var currentRect) && currentRect.Width > 0)
            {
                double bestScore = double.MaxValue;
                int cx = currentRect.CenterX;
                int cy = currentRect.CenterY;

                foreach (var candidate in allWindows.Where(w => w.Handle != fgHwnd))
                {
                    candidate.RefreshBounds();
                    int tx = candidate.Bounds.CenterX;
                    int ty = candidate.Bounds.CenterY;

                    int relX = tx - cx;
                    int relY = ty - cy;

                    // Check if the candidate is in the desired direction
                    bool inDirection = dx switch
                    {
                        -1 => relX < -10, // at least 10px to the left
                        1 => relX > 10,
                        _ => true
                    } && dy switch
                    {
                        -1 => relY < -10,
                        1 => relY > 10,
                        _ => true
                    };

                    if (!inDirection) continue;

                    double dist = Math.Sqrt(relX * relX + relY * relY);
                    double axisPenalty = dx != 0
                        ? Math.Abs(relY) * 2.0
                        : Math.Abs(relX) * 2.0;

                    double score = dist + axisPenalty;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }

            // No fallback cycling — if nothing is found in that direction, stay put
            if (best == null)
            {
                Logger.Instance.Debug("FocusInDirection: no candidate in direction, staying put");
                return;
            }

            NativeMethods.ForceForegroundWindow(best.Handle);
            _workspaceManager.UpdateFocus(best.Handle);
            Logger.Instance.Debug($"Focus moved to: {best}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error in FocusInDirection", ex);
        }
    }

    // ──────────────── Window Moving ────────────────

    public void MoveLeft() => SwapInDirection(-1, 0);
    public void MoveRight() => SwapInDirection(1, 0);
    public void MoveUp() => SwapInDirection(0, -1);
    public void MoveDown() => SwapInDirection(0, 1);

    // ──────────────── Window Resizing ────────────────

    public void ResizeLeft() => ResizeInDirection(-1, 0);
    public void ResizeRight() => ResizeInDirection(1, 0);
    public void ResizeUp() => ResizeInDirection(0, -1);
    public void ResizeDown() => ResizeInDirection(0, 1);

    /// <summary>
    /// Resize the focused window by adjusting the BSP split ratio in the given direction.
    /// Other windows adjust automatically.
    /// </summary>
    private void ResizeInDirection(int dx, int dy)
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            int monIdx = _workspaceManager.GetFocusedMonitorIndex();
            var ws = _workspaceManager.GetActiveWorkspace(monIdx);
            if (ws == null) return;

            if (_tilingEngine.ResizeInDirection(ws, hwnd, dx, dy, 0.02))
            {
                _tilingEngine.TileWorkspace(ws);
                Logger.Instance.Debug($"Resized window {hwnd} in direction ({dx},{dy})");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error in ResizeInDirection", ex);
        }
    }

    /// <summary>
    /// Swap the focused window with the nearest window in the given direction.
    /// </summary>
    private void SwapInDirection(int dx, int dy)
    {
        try
        {
            var fgHwnd = NativeMethods.GetForegroundWindow();
            if (!NativeMethods.GetWindowRect(fgHwnd, out var currentRect)) return;

            int monIdx = _workspaceManager.GetFocusedMonitorIndex();
            var ws = _workspaceManager.GetActiveWorkspace(monIdx);
            if (ws == null) return;

            var candidates = ws.Windows
                .Where(w => w.Handle != fgHwnd && !w.IsMinimized && !w.IsFloating)
                .ToList();

            // Empty candidate list is handled below — we may still move to an adjacent monitor.

            ManagedWindow? best = null;
            double bestDist = double.MaxValue;

            int cx = currentRect.CenterX;
            int cy = currentRect.CenterY;

            foreach (var candidate in candidates)
            {
                candidate.RefreshBounds();
                int relX = candidate.Bounds.CenterX - cx;
                int relY = candidate.Bounds.CenterY - cy;

                bool inDirection = dx switch
                {
                    -1 => relX < 0,
                    1 => relX > 0,
                    _ => true
                } && dy switch
                {
                    -1 => relY < 0,
                    1 => relY > 0,
                    _ => true
                };

                if (!inDirection) continue;

                double dist = Math.Sqrt(relX * relX + relY * relY);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }

            if (best != null)
            {
                _tilingEngine.SwapWindows(ws, fgHwnd, best.Handle);
                _tilingEngine.TileWorkspace(ws);
                Logger.Instance.Debug($"Swapped window with: {best}");
            }
            else
            {
                // No swap target in this direction on the current workspace —
                // move the window to the nearest monitor in that direction.
                var adjMon = GetAdjacentMonitor(monIdx, dx, dy);
                if (adjMon != null)
                {
                    _workspaceManager.MoveWindowToMonitor(fgHwnd, adjMon.Index);
                    NativeMethods.ForceForegroundWindow(fgHwnd);
                    Logger.Instance.Debug($"Moved window to adjacent monitor {adjMon.Index}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error in SwapInDirection", ex);
        }
    }

    // ──────────────── Window Actions ────────────────

    /// <summary>
    /// Close the currently focused window.
    /// </summary>
    public void CloseWindow()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            Logger.Instance.Debug($"Sent WM_CLOSE to {hwnd}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error closing window", ex);
        }
    }

    /// <summary>
    /// Toggle floating state for the focused window.
    /// </summary>
    public void ToggleFloat()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            var window = _windowTracker.GetWindow(hwnd);
            if (window == null) return;

            int monIdx = _workspaceManager.GetFocusedMonitorIndex();
            var ws = _workspaceManager.GetActiveWorkspace(monIdx);
            if (ws == null) return;

            window.IsFloating = !window.IsFloating;

            if (window.IsFloating)
            {
                // Remove from BSP tree, restore saved bounds
                _tilingEngine.RemoveWindow(ws, hwnd);

                if (window.SavedBounds.HasValue)
                {
                    TilingEngine.ApplyWindowPosition(hwnd, window.SavedBounds.Value);
                }
                else
                {
                    // Center on monitor at 60% size
                    var mon = _monitorManager.GetByIndex(monIdx);
                    if (mon != null)
                    {
                        var wa = mon.EffectiveWorkArea;
                        int w = (int)(wa.Width * 0.6);
                        int h = (int)(wa.Height * 0.6);
                        int x = wa.Left + (wa.Width - w) / 2;
                        int y = wa.Top + (wa.Height - h) / 2;
                        TilingEngine.ApplyWindowPosition(hwnd, new NativeMethods.RECT(x, y, x + w, y + h));
                    }
                }

                Logger.Instance.Info($"Window {hwnd} set to floating");
            }
            else
            {
                // Save current bounds, add back to BSP tree
                NativeMethods.GetWindowRect(hwnd, out var rect);
                window.SavedBounds = rect;
                _tilingEngine.AddWindow(ws, hwnd);
                Logger.Instance.Info($"Window {hwnd} set to tiled");
            }

            _tilingEngine.TileWorkspace(ws);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error toggling float", ex);
        }
    }

    /// <summary>
    /// Toggle fullscreen for the focused window.
    /// </summary>
    public void ToggleFullscreen()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            var window = _windowTracker.GetWindow(hwnd);
            if (window == null) return;

            int monIdx = _workspaceManager.GetFocusedMonitorIndex();
            var ws = _workspaceManager.GetActiveWorkspace(monIdx);
            if (ws == null) return;

            window.IsFullscreen = !window.IsFullscreen;

            if (window.IsFullscreen)
            {
                NativeMethods.GetWindowRect(hwnd, out var rect);
                window.SavedBounds = rect;

                // Fill the entire monitor work area (or full bounds for true fullscreen)
                var mon = _monitorManager.GetByIndex(monIdx);
                if (mon != null)
                {
                    var fs = mon.EffectiveWorkArea;
                    TilingEngine.ApplyWindowPosition(hwnd, fs);
                }

                Logger.Instance.Info($"Window {hwnd} set to fullscreen");
            }
            else
            {
                window.IsFullscreen = false;
                // Re-add to tiling
                _tilingEngine.RebuildTree(ws);
                _tilingEngine.TileWorkspace(ws);
                Logger.Instance.Info($"Window {hwnd} exited fullscreen");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error toggling fullscreen", ex);
        }
    }

    // ──────────────── Workspace / Monitor Switching ────────────────

    /// <summary>
    /// Switch to workspace N. In monitor_bound mode, this focuses Monitor N.
    /// In virtual mode, this switches the virtual workspace on the current monitor.
    /// </summary>
    public void SwitchToWorkspace(int workspaceIndex)
    {
        if (_workspaceMode.Equals("monitor_bound", StringComparison.OrdinalIgnoreCase))
        {
            // Workspace N = Monitor N: focus a window on that monitor
            FocusMonitor(workspaceIndex);
        }
        else
        {
            // Virtual mode: switch virtual workspace on current monitor
            int monIdx = _workspaceManager.GetFocusedMonitorIndex();
            _workspaceManager.SwitchWorkspace(monIdx, workspaceIndex);
        }
    }

    /// <summary>
    /// Move focused window to workspace N. In monitor_bound mode, moves to Monitor N.
    /// </summary>
    public void MoveToWorkspace(int workspaceIndex)
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        if (_workspaceMode.Equals("monitor_bound", StringComparison.OrdinalIgnoreCase))
        {
            // Workspace N = Monitor N: move window to that monitor
            _workspaceManager.MoveWindowToMonitor(hwnd, workspaceIndex);
        }
        else
        {
            // Virtual mode: move to virtual workspace on current monitor
            _workspaceManager.MoveWindowToWorkspace(hwnd, workspaceIndex);
        }

        // Follow focus so the window stays active on the target
        NativeMethods.ForceForegroundWindow(hwnd);
    }

    /// <summary>
    /// Focus the last-focused window on a specific monitor.
    /// </summary>
    private void FocusMonitor(int monitorIndex)
    {
        try
        {
            var mon = _monitorManager.GetByIndex(monitorIndex);
            if (mon == null)
            {
                Logger.Instance.Debug($"FocusMonitor: monitor {monitorIndex} not found");
                return;
            }

            var ws = _workspaceManager.GetActiveWorkspace(monitorIndex);
            if (ws == null) return;

            if (ws.FocusedWindow != null && NativeMethods.IsWindow(ws.FocusedWindow.Handle))
            {
                NativeMethods.ForceForegroundWindow(ws.FocusedWindow.Handle);
                Logger.Instance.Debug($"Focused monitor {monitorIndex}: {ws.FocusedWindow}");
            }
            else if (ws.Windows.Count > 0)
            {
                var firstVisible = ws.Windows.FirstOrDefault(w =>
                    !w.IsMinimized && NativeMethods.IsWindow(w.Handle));
                if (firstVisible != null)
                {
                    ws.FocusedWindow = firstVisible;
                    NativeMethods.ForceForegroundWindow(firstVisible.Handle);
                    Logger.Instance.Debug($"Focused monitor {monitorIndex}: {firstVisible}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"Error focusing monitor {monitorIndex}", ex);
        }
    }

    public void MoveToMonitor(int monitorIndex)
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        _workspaceManager.MoveWindowToMonitor(hwnd, monitorIndex);
        // Follow focus so the window stays active on the target monitor
        NativeMethods.ForceForegroundWindow(hwnd);
    }

    // ──────────────── Launch Apps ────────────────

    public void LaunchExplorer()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true,
            });
            Logger.Instance.Debug("Launched Windows Explorer");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to launch explorer", ex);
        }
    }

    public void LaunchTerminal()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _terminalCommand,
                UseShellExecute = true,
            });
            Logger.Instance.Debug($"Launched terminal: {_terminalCommand}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"Failed to launch terminal '{_terminalCommand}'", ex);
        }
    }

    // ──────────────── Helpers ────────────────

    /// <summary>
    /// Returns the nearest monitor in the given direction relative to <paramref name="currentMonIdx"/>.
    /// Uses center-to-center distance with a directional filter.
    /// Returns null when no monitor exists in that direction.
    /// </summary>
    private MonitorInfo? GetAdjacentMonitor(int currentMonIdx, int dx, int dy)
    {
        var currentMon = _monitorManager.GetByIndex(currentMonIdx);
        if (currentMon == null) return null;

        int cx = currentMon.Bounds.CenterX;
        int cy = currentMon.Bounds.CenterY;

        MonitorInfo? best = null;
        double bestScore = double.MaxValue;

        foreach (var mon in _monitorManager.Monitors)
        {
            if (mon.Index == currentMonIdx) continue;

            int relX = mon.Bounds.CenterX - cx;
            int relY = mon.Bounds.CenterY - cy;

            bool inDir = dx switch { -1 => relX < 0, 1 => relX > 0, _ => true }
                      && dy switch { -1 => relY < 0, 1 => relY > 0, _ => true };

            if (!inDir) continue;

            double score = Math.Sqrt(relX * relX + relY * relY);
            if (score < bestScore) { bestScore = score; best = mon; }
        }

        return best;
    }
}
