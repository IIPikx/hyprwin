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
    /// Move focus to the nearest window in the given direction — Hyprland-style edge-biased scoring.
    /// Uses DWM extended frame bounds (visual rect without shadow) for accurate hit testing,
    /// same approach as Komorebi's window_rect().
    /// </summary>
    private void FocusInDirection(int dx, int dy)
    {
        try
        {
            var fgHwnd = NativeMethods.GetForegroundWindow();
            Logger.Instance.Debug($"FocusInDirection({dx},{dy}) — foreground={fgHwnd}");

            var focusedRect = GetVisualRect(fgHwnd);
            if (focusedRect.Width <= 0) return;

            var candidates = _monitorManager.Monitors
                .SelectMany(mon =>
                    _workspaceManager.GetActiveWorkspace(mon.Index)?.Windows
                    ?? Enumerable.Empty<ManagedWindow>())
                .Where(w =>
                {
                    if (w.Handle == fgHwnd) return false;
                    if (!NativeMethods.IsWindow(w.Handle)) return false;
                    if (!NativeMethods.IsWindowVisible(w.Handle)) return false;
                    w.IsMinimized = NativeMethods.IsIconic(w.Handle);
                    if (w.IsMinimized) return false;
                    w.RefreshBounds();
                    if (w.Bounds.Left < -5000 || w.Bounds.Top < -5000) return false;
                    return true;
                })
                .ToList();

            Logger.Instance.Debug($"FocusInDirection({dx},{dy}): {candidates.Count} candidates across all monitors");
            if (candidates.Count == 0) return;

            ManagedWindow? best = null;
            double bestScore = double.MaxValue;

            foreach (var candidate in candidates)
            {
                var cb = GetVisualRect(candidate.Handle);

                if (cb.Left < -5000 || cb.Top < -5000) continue;
                if (cb.Width <= 0 || cb.Height <= 0) continue;

                // ── Hyprland-style direction check ──────────────────────────────
                // Use EDGE distances rather than center-to-center.
                // Direction is satisfied when the candidate's leading edge
                // is strictly past the focused window's trailing edge in that axis.
                bool inDirection;
                int primaryGap;    // pixels between the two facing edges (can be 0 for adjacent)
                int perpendicular; // off-axis offset between centers

                if (dx != 0)
                {
                    int focusedEdge  = dx > 0 ? focusedRect.Right  : focusedRect.Left;
                    int candidatEdge = dx > 0 ? cb.Left             : cb.Right;
                    int relEdge      = (candidatEdge - focusedEdge) * dx; // positive = in direction
                    inDirection = relEdge > -8; // allow 8px overlap tolerance for adjacent tiles
                    primaryGap  = Math.Max(0, relEdge);
                    perpendicular = Math.Abs(cb.CenterY - focusedRect.CenterY);
                }
                else
                {
                    int focusedEdge  = dy > 0 ? focusedRect.Bottom : focusedRect.Top;
                    int candidatEdge = dy > 0 ? cb.Top              : cb.Bottom;
                    int relEdge      = (candidatEdge - focusedEdge) * dy;
                    inDirection = relEdge > -8;
                    primaryGap  = Math.Max(0, relEdge);
                    perpendicular = Math.Abs(cb.CenterX - focusedRect.CenterX);
                }

                if (!inDirection) continue;

                // Score = gap to the facing edge  +  perpendicular offset (weighted)
                // This mirrors Hyprland's "closest in direction" heuristic:
                // windows directly in line are always preferred over diagonal ones.
                double score = primaryGap + perpendicular * 1.5;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best == null)
            {
                Logger.Instance.Debug("FocusInDirection: no candidate in direction, staying put");
                return;
            }

            NativeMethods.ForceForegroundWindow(best.Handle);
            _workspaceManager.UpdateFocus(best.Handle);

            // Warp cursor to center of newly focused window (Hyprland-style)
            var bestRect = GetVisualRect(best.Handle);
            NativeMethods.SetCursorPos(bestRect.CenterX, bestRect.CenterY);

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

    // ──────────────── Workspace Mirror/Flip ────────────────

    /// <summary>
    /// Mirror all windows horizontally (left↔right) in the active workspace.
    /// </summary>
    public void SwapHorizontal()
    {
        try
        {
            int monIdx = _workspaceManager.GetFocusedMonitorIndex();
            var ws = _workspaceManager.GetActiveWorkspace(monIdx);
            if (ws == null) return;

            _tilingEngine.MirrorHorizontal(ws);
            _tilingEngine.TileWorkspace(ws, animate: false);
            Logger.Instance.Debug($"Mirrored workspace {ws.Id} horizontally");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error in SwapHorizontal", ex);
        }
    }

    /// <summary>
    /// Rotate the active window's parent BSP split to Vertical (side-by-side, SUPER+X).
    /// </summary>
    public void RotateSplitVertical()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            int monIdx = _workspaceManager.GetFocusedMonitorIndex();
            var ws = _workspaceManager.GetActiveWorkspace(monIdx);
            if (ws == null) return;
            if (_tilingEngine.RotateSplitToDirection(ws, hwnd, BspNode.SplitDirection.Vertical))
            {
                _tilingEngine.TileWorkspace(ws, animate: false);
                Logger.Instance.Debug($"Rotated split to Vertical for window {hwnd}");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error in RotateSplitVertical", ex);
        }
    }

    /// <summary>
    /// Rotate the active window's parent BSP split to Horizontal (stacked, SUPER+Y).
    /// </summary>
    public void RotateSplitHorizontal()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            int monIdx = _workspaceManager.GetFocusedMonitorIndex();
            var ws = _workspaceManager.GetActiveWorkspace(monIdx);
            if (ws == null) return;
            if (_tilingEngine.RotateSplitToDirection(ws, hwnd, BspNode.SplitDirection.Horizontal))
            {
                _tilingEngine.TileWorkspace(ws, animate: false);
                Logger.Instance.Debug($"Rotated split to Horizontal for window {hwnd}");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error in RotateSplitHorizontal", ex);
        }
    }

    /// <summary>
    /// Mirror all windows vertically (top↔bottom) in the active workspace.
    /// </summary>
    public void SwapVertical()
    {
        try
        {
            int monIdx = _workspaceManager.GetFocusedMonitorIndex();
            var ws = _workspaceManager.GetActiveWorkspace(monIdx);
            if (ws == null) return;

            _tilingEngine.MirrorVertical(ws);
            _tilingEngine.TileWorkspace(ws, animate: false);
            Logger.Instance.Debug($"Mirrored workspace {ws.Id} vertically");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error in SwapVertical", ex);
        }
    }

    // ──────────────── Window Resizing ────────────────

    public void ResizeLeft() => ResizeInDirection(-1, 0);
    public void ResizeRight() => ResizeInDirection(1, 0);
    public void ResizeUp() => ResizeInDirection(0, -1);
    public void ResizeDown() => ResizeInDirection(0, 1);

    /// <summary>
    /// Resize the focused window by adjusting the nearest BSP split ratio in the given direction.
    /// All sibling windows in the workspace retile immediately (Hyprland-style, no animation).
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

            // Step size: 3.5 % of the relevant split.
            // Immediate retile (animate:false) so rapid key-repeats are always
            // applied to the up-to-date layout — no stale mid-animation positions.
            if (_tilingEngine.ResizeInDirection(ws, hwnd, dx, dy, 0.035))
            {
                _tilingEngine.TileWorkspace(ws, animate: false);
                Logger.Instance.Debug($"Resized window {hwnd} in direction ({dx},{dy})");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error in ResizeInDirection", ex);
        }
    }

    /// <summary>
    /// Move the focused window in the given direction — Hyprland-style:
    /// · Within workspace: swap with the nearest tiling neighbour (BSP handle swap + retile).
    /// · At workspace edge: move the window to the adjacent monitor's active workspace.
    /// All retiling is done without animation so that positions are settled before focus moves.
    /// </summary>
    private void SwapInDirection(int dx, int dy)
    {
        try
        {
            var fgHwnd = NativeMethods.GetForegroundWindow();
            var currentRect = GetVisualRect(fgHwnd);
            if (currentRect.Width <= 0) return;

            int monIdx = _workspaceManager.GetFocusedMonitorIndex();
            var ws    = _workspaceManager.GetActiveWorkspace(monIdx);
            if (ws == null) return;

            // ── 1. Look for a swap candidate on the same workspace ──────────────
            ManagedWindow? best   = null;
            double         bestScore = double.MaxValue;

            var localCandidates = ws.Windows
                .Where(w =>
                {
                    if (w.Handle == fgHwnd || w.IsFloating) return false;
                    w.IsMinimized = NativeMethods.IsIconic(w.Handle); // live refresh
                    return !w.IsMinimized;
                })
                .ToList();

            foreach (var candidate in localCandidates)
            {
                var cb = GetVisualRect(candidate.Handle);
                if (cb.Left < -5000 || cb.Top < -5000) continue;
                if (cb.Width <= 0 || cb.Height <= 0) continue;

                // Edge-based direction check (same as FocusInDirection)
                bool inDirection;
                int  primaryGap;
                int  perpendicular;

                if (dx != 0)
                {
                    int fEdge = dx > 0 ? currentRect.Right : currentRect.Left;
                    int cEdge = dx > 0 ? cb.Left            : cb.Right;
                    int rel   = (cEdge - fEdge) * dx;
                    inDirection  = rel > -8;
                    primaryGap   = Math.Max(0, rel);
                    perpendicular = Math.Abs(cb.CenterY - currentRect.CenterY);
                }
                else
                {
                    int fEdge = dy > 0 ? currentRect.Bottom : currentRect.Top;
                    int cEdge = dy > 0 ? cb.Top              : cb.Bottom;
                    int rel   = (cEdge - fEdge) * dy;
                    inDirection  = rel > -8;
                    primaryGap   = Math.Max(0, rel);
                    perpendicular = Math.Abs(cb.CenterX - currentRect.CenterX);
                }

                if (!inDirection) continue;

                double score = primaryGap + perpendicular * 1.5;
                if (score < bestScore) { bestScore = score; best = candidate; }
            }

            if (best != null)
            {
                // Swap the two window handles in the BSP tree and retile immediately.
                // animate:false ensures both windows snap to their new positions
                // before any follow-up key-press is processed.
                _tilingEngine.SwapWindows(ws, fgHwnd, best.Handle);
                _tilingEngine.TileWorkspace(ws, animate: false);
                // Keep focus on the moved window (which now occupies the neighbour's old slot)
                NativeMethods.ForceForegroundWindow(fgHwnd);
                Logger.Instance.Debug($"Swapped window with: {best}");
                return;
            }

            // ── 2. No candidate on this workspace → move to adjacent monitor ────
            var adjMon = GetAdjacentMonitor(monIdx, dx, dy);
            if (adjMon == null) return;

            // Capture source workspace now (before the move changes ownership).
            var sourceWs = ws;
            var targetWs = _workspaceManager.GetActiveWorkspace(adjMon.Index);
            if (targetWs == null) return;

            // Transfer the window's workspace/monitor membership.
            // fireRetile:false — we handle retiling explicitly below (animate:false is required
            // so that ForceForegroundWindow finds windows at their final positions)
            _workspaceManager.MoveWindowToMonitor(fgHwnd, adjMon.Index, fireRetile: false);

            // Retile both workspaces immediately — no animation.
            // This settles all window positions before ForceForeground is called,
            // avoiding the "window still mid-animation when focused" glitch.
            _tilingEngine.RebuildTree(sourceWs);
            _tilingEngine.TileWorkspace(sourceWs, animate: false);
            _tilingEngine.RebuildTree(targetWs);
            _tilingEngine.TileWorkspace(targetWs, animate: false);

            NativeMethods.ForceForegroundWindow(fgHwnd);
            Logger.Instance.Debug($"Moved window to monitor {adjMon.Index}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error in SwapInDirection", ex);
        }
    }

    // ──────────────── Window Actions ────────────────

    /// <summary>
    /// Close the currently focused window.
    /// Sends WM_CLOSE first; if the window survives, terminates the process.
    /// </summary>
    public void CloseWindow()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            // Capture process info before sending close
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);

            NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            Logger.Instance.Debug($"Sent WM_CLOSE to {hwnd} (pid={pid})");

            // Check if the window survives WM_CLOSE and force-kill if needed
            if (pid != 0)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    if (NativeMethods.IsWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd))
                    {
                        try
                        {
                            var proc = Process.GetProcessById((int)pid);
                            proc.Kill();
                            Logger.Instance.Info($"Force-killed process {proc.ProcessName} (pid={pid}) — did not respond to WM_CLOSE");
                        }
                        catch (ArgumentException) { /* Process already exited */ }
                        catch (Exception ex) { Logger.Instance.Error($"Error force-killing pid {pid}", ex); }
                    }
                });
            }
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

                // Fill the entire monitor bounds for true fullscreen (covers top bar)
                var mon = _monitorManager.GetByIndex(monIdx);
                if (mon != null)
                {
                    var fs = mon.Bounds;
                    TilingEngine.ApplyWindowPosition(hwnd, fs);
                    // Place fullscreen window above the top bar
                    NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 
                        fs.Left, fs.Top, fs.Width, fs.Height,
                        NativeMethods.SWP_NOACTIVATE);
                }

                Logger.Instance.Info($"Window {hwnd} set to fullscreen");
            }
            else
            {
                window.IsFullscreen = false;
                // Restore from topmost and re-add to tiling
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
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

        int sourceMonIdx = _workspaceManager.GetFocusedMonitorIndex();
        var sourceWs     = _workspaceManager.GetActiveWorkspace(sourceMonIdx);

        if (_workspaceMode.Equals("monitor_bound", StringComparison.OrdinalIgnoreCase))
        {
            var targetWs = _workspaceManager.GetActiveWorkspace(workspaceIndex);
            _workspaceManager.MoveWindowToMonitor(hwnd, workspaceIndex, fireRetile: false);

            if (sourceWs != null)
            {
                _tilingEngine.RebuildTree(sourceWs);
                _tilingEngine.TileWorkspace(sourceWs, animate: false);
            }
            if (targetWs != null)
            {
                _tilingEngine.RebuildTree(targetWs);
                _tilingEngine.TileWorkspace(targetWs, animate: false);
            }
        }
        else
        {
            _workspaceManager.MoveWindowToWorkspace(hwnd, workspaceIndex);
            // Automatically follow the window to its new workspace
            _workspaceManager.SwitchWorkspace(sourceMonIdx, workspaceIndex);
        }

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

        int sourceMonIdx = _workspaceManager.GetFocusedMonitorIndex();
        var sourceWs     = _workspaceManager.GetActiveWorkspace(sourceMonIdx);
        var targetWs     = _workspaceManager.GetActiveWorkspace(monitorIndex);

        _workspaceManager.MoveWindowToMonitor(hwnd, monitorIndex, fireRetile: false);

        if (sourceWs != null)
        {
            _tilingEngine.RebuildTree(sourceWs);
            _tilingEngine.TileWorkspace(sourceWs, animate: false);
        }
        if (targetWs != null)
        {
            _tilingEngine.RebuildTree(targetWs);
            _tilingEngine.TileWorkspace(targetWs, animate: false);
        }

        NativeMethods.ForceForegroundWindow(hwnd);
    }

    // ──────────────── Launch Apps ────────────────

    /// <summary>
    /// Minimize all non-minimized windows on the current workspace (SUPER+D).
    /// </summary>
    public void MinimizeAll()
    {
        try
        {
            int monIdx = _workspaceManager.GetFocusedMonitorIndex();
            var ws = _workspaceManager.GetActiveWorkspace(monIdx);
            if (ws == null) return;

            foreach (var w in ws.Windows)
            {
                if (!w.IsMinimized && NativeMethods.IsWindow(w.Handle)
                    && NativeMethods.IsWindowVisible(w.Handle))
                {
                    NativeMethods.ShowWindow(w.Handle, NativeMethods.SW_MINIMIZE);
                    w.IsMinimized = true;
                }
            }

            Logger.Instance.Debug($"MinimizeAll: minimized {ws.Windows.Count} window(s) on workspace {ws.Id}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error in MinimizeAll", ex);
        }
    }

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

    public void TakeScreenshot()
    {
        try
        {
            // Launch the Windows Snipping Tool overlay (SUPER+SHIFT+S equivalent).
            // ms-screenclip: opens the full snip UI without needing keyboard input.
            Process.Start(new ProcessStartInfo
            {
                FileName        = "ms-screenclip:",
                UseShellExecute = true,
            });
            Logger.Instance.Debug("Launched screenshot tool");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to launch screenshot tool", ex);
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

    /// <summary>
    /// Launch Windows Task Manager. Registered as an explicit keybind because
    /// the low-level keyboard hook can interfere with the native Ctrl+Shift+Esc shortcut.
    /// </summary>
    public void LaunchTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "taskmgr.exe",
                UseShellExecute = true,
            });
            Logger.Instance.Debug("Launched Task Manager");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to launch Task Manager", ex);
        }
    }

    /// <summary>
    /// Launch an arbitrary program. Used by custom [[launch]] shortcut entries.
    /// </summary>
    public void LaunchProgram(string command, string args = "")
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = true,
            };
            if (!string.IsNullOrWhiteSpace(args))
                psi.Arguments = args;

            Process.Start(psi);
            Logger.Instance.Debug($"Launched program: {command} {args}".TrimEnd());
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"Failed to launch program '{command}'", ex);
        }
    }

    // ──────────────── Helpers ────────────────

    /// <summary>
    /// Get the visual rect of a window (excluding invisible DWM shadow borders).
    /// Komorebi uses DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) for this,
    /// falling back to GetWindowRect if DWM fails.
    /// This gives the actual on-screen painted area, which is critical for
    /// accurate direction-based focus/swap operations.
    /// </summary>
    private static NativeMethods.RECT GetVisualRect(IntPtr hwnd)
    {
        var extRect = NativeMethods.GetExtendedFrameBounds(hwnd);
        if (extRect.Width > 0 && extRect.Height > 0)
            return extRect;
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return rect;
    }

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
