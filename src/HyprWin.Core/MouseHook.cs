using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Low-level mouse hook for Hyprland-style interactive window management:
/// • SUPER + Left Click (Drag): Move/swap window interactively with cursor
/// • SUPER + Right Click (Drag): Dynamically resize window/BSP split with cursor
/// </summary>
public sealed class MouseHook : IDisposable
{
    private readonly WindowTracker _windowTracker;
    private readonly WorkspaceManager _workspaceManager;
    private readonly TilingEngine _tilingEngine;
    private readonly MonitorManager _monitorManager;

    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelMouseProc? _hookProc;
    private bool _disposed;

    private enum MouseAction { None, Dragging, Resizing }
    private MouseAction _currentAction = MouseAction.None;
    private IntPtr _draggedHwnd = IntPtr.Zero;
    private NativeMethods.POINT _startMousePos;
    private NativeMethods.RECT _startWindowBounds;
    private ManagedWindow? _draggedWindow;
    private Workspace? _draggedWorkspace;

    public MouseHook(
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

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;

        _hookProc = HookCallback;
        using var curProc = Process.GetCurrentProcess();
        using var curMod = curProc.MainModule;
        var hMod = NativeMethods.GetModuleHandle(curMod?.ModuleName);

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _hookProc,
            hMod,
            0);

        if (_hookId != IntPtr.Zero)
            Logger.Instance.Info("Mouse hook installed (SUPER+LMB drag, SUPER+RMB resize)");
        else
            Logger.Instance.Warn("Failed to install mouse hook");
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            Logger.Instance.Info("Mouse hook uninstalled");
        }
    }

    private static bool IsSuperDown()
    {
        return (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0 ||
               (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var mouseInfo = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            // Fast path: if no action is in progress and SUPER is not down, pass immediately
            if (_currentAction == MouseAction.None && !IsSuperDown())
            {
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            switch (msg)
            {
                case NativeMethods.WM_LBUTTONDOWN:
                    if (IsSuperDown() && StartDrag(mouseInfo.pt))
                        return (IntPtr)1; // swallow click
                    break;

                case NativeMethods.WM_RBUTTONDOWN:
                    if (IsSuperDown() && StartResize(mouseInfo.pt))
                        return (IntPtr)1; // swallow click
                    break;

                case NativeMethods.WM_MOUSEMOVE:
                    if (_currentAction == MouseAction.Dragging)
                    {
                        OnMouseMoveDrag(mouseInfo.pt);
                        return (IntPtr)1;
                    }
                    else if (_currentAction == MouseAction.Resizing)
                    {
                        OnMouseMoveResize(mouseInfo.pt);
                        return (IntPtr)1;
                    }
                    break;

                case NativeMethods.WM_LBUTTONUP:
                    if (_currentAction == MouseAction.Dragging)
                    {
                        EndDrag(mouseInfo.pt);
                        return (IntPtr)1;
                    }
                    break;

                case NativeMethods.WM_RBUTTONUP:
                    if (_currentAction == MouseAction.Resizing)
                    {
                        EndResize(mouseInfo.pt);
                        return (IntPtr)1;
                    }
                    break;
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private IntPtr GetTargetWindow(NativeMethods.POINT pt)
    {
        var rawHwnd = NativeMethods.WindowFromPoint(pt);
        if (rawHwnd == IntPtr.Zero) return IntPtr.Zero;

        var rootHwnd = NativeMethods.GetAncestor(rawHwnd, NativeMethods.GA_ROOT);
        if (rootHwnd == IntPtr.Zero) rootHwnd = rawHwnd;

        var mw = _windowTracker.GetWindow(rootHwnd);
        return mw != null ? rootHwnd : IntPtr.Zero;
    }

    private bool StartDrag(NativeMethods.POINT pt)
    {
        var hwnd = GetTargetWindow(pt);
        if (hwnd == IntPtr.Zero) return false;

        _draggedHwnd = hwnd;
        _draggedWindow = _windowTracker.GetWindow(hwnd);
        _draggedWorkspace = _workspaceManager.FindWorkspaceForWindow(hwnd);
        _startMousePos = pt;

        NativeMethods.GetWindowRect(hwnd, out _startWindowBounds);
        _currentAction = MouseAction.Dragging;

        NativeMethods.ForceForegroundWindow(hwnd);
        _workspaceManager.UpdateFocus(hwnd);

        Logger.Instance.Debug($"Started mouse drag for window {hwnd}");
        return true;
    }

    private void OnMouseMoveDrag(NativeMethods.POINT pt)
    {
        if (_draggedHwnd == IntPtr.Zero || _draggedWindow == null) return;

        int dx = pt.X - _startMousePos.X;
        int dy = pt.Y - _startMousePos.Y;

        if (_draggedWindow.IsFloating)
        {
            int newX = _startWindowBounds.Left + dx;
            int newY = _startWindowBounds.Top + dy;
            NativeMethods.SetWindowPos(_draggedHwnd, IntPtr.Zero,
                newX, newY, _startWindowBounds.Width, _startWindowBounds.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_DEFERERASE);
        }
    }

    private void EndDrag(NativeMethods.POINT pt)
    {
        if (_draggedHwnd == IntPtr.Zero)
        {
            _currentAction = MouseAction.None;
            return;
        }

        try
        {
            if (_draggedWindow != null && !_draggedWindow.IsFloating && _draggedWorkspace != null)
            {
                var targetHwnd = GetTargetWindow(pt);
                if (targetHwnd != IntPtr.Zero && targetHwnd != _draggedHwnd)
                {
                    var targetWs = _workspaceManager.FindWorkspaceForWindow(targetHwnd);
                    if (targetWs == _draggedWorkspace)
                    {
                        _tilingEngine.SwapWindows(_draggedWorkspace, _draggedHwnd, targetHwnd);
                        _tilingEngine.TileWorkspace(_draggedWorkspace, animate: true);
                        Logger.Instance.Debug($"Mouse-swapped window {_draggedHwnd} with {targetHwnd}");
                    }
                }
            }
        }
        finally
        {
            _currentAction = MouseAction.None;
            _draggedHwnd = IntPtr.Zero;
            _draggedWindow = null;
            _draggedWorkspace = null;
        }
    }

    private bool StartResize(NativeMethods.POINT pt)
    {
        var hwnd = GetTargetWindow(pt);
        if (hwnd == IntPtr.Zero) return false;

        _draggedHwnd = hwnd;
        _draggedWindow = _windowTracker.GetWindow(hwnd);
        _draggedWorkspace = _workspaceManager.FindWorkspaceForWindow(hwnd);
        _startMousePos = pt;

        NativeMethods.GetWindowRect(hwnd, out _startWindowBounds);
        _currentAction = MouseAction.Resizing;

        NativeMethods.ForceForegroundWindow(hwnd);
        _workspaceManager.UpdateFocus(hwnd);

        Logger.Instance.Debug($"Started mouse resize for window {hwnd}");
        return true;
    }

    private void OnMouseMoveResize(NativeMethods.POINT pt)
    {
        if (_draggedHwnd == IntPtr.Zero || _draggedWindow == null) return;

        int dx = pt.X - _startMousePos.X;
        int dy = pt.Y - _startMousePos.Y;

        if (_draggedWindow.IsFloating)
        {
            int newW = Math.Max(200, _startWindowBounds.Width + dx);
            int newH = Math.Max(150, _startWindowBounds.Height + dy);
            NativeMethods.SetWindowPos(_draggedHwnd, IntPtr.Zero,
                _startWindowBounds.Left, _startWindowBounds.Top, newW, newH,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_DEFERERASE);
        }
        else if (_draggedWorkspace != null)
        {
            int dirX = Math.Abs(dx) > 10 ? (dx > 0 ? 1 : -1) : 0;
            int dirY = Math.Abs(dy) > 10 ? (dy > 0 ? 1 : -1) : 0;

            if (dirX != 0 || dirY != 0)
            {
                if (_tilingEngine.ResizeInDirection(_draggedWorkspace, _draggedHwnd, dirX, dirY, 0.015))
                {
                    _tilingEngine.TileWorkspace(_draggedWorkspace, animate: false);
                    _startMousePos = pt;
                }
            }
        }
    }

    private void EndResize(NativeMethods.POINT pt)
    {
        try
        {
            if (_draggedWorkspace != null && _draggedWindow is { IsFloating: false })
            {
                _tilingEngine.TileWorkspace(_draggedWorkspace, animate: false);
            }
        }
        finally
        {
            _currentAction = MouseAction.None;
            _draggedHwnd = IntPtr.Zero;
            _draggedWindow = null;
            _draggedWorkspace = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Uninstall();
    }
}
