using System.Diagnostics;
using System.Windows;
using H.NotifyIcon;
using HyprWin.Core;
using HyprWin.Core.Configuration;

namespace HyprWin.App;

/// <summary>
/// HyprWin application entry point.
/// Runs as a tray-only WPF application (no main window).
/// Orchestrates all subsystems: config, hooks, tiling, top bar, taskbar hiding.
/// </summary>
public partial class App : Application
{
    // ──────────────── Core Services ────────────────
    private ConfigManager _configManager = null!;
    private KeyboardHook _keyboardHook = null!;
    private MonitorManager _monitorManager = null!;
    private WindowTracker _windowTracker = null!;
    private WorkspaceManager _workspaceManager = null!;
    private TilingEngine _tilingEngine = null!;
    private WindowDispatcher _dispatcher = null!;
    private AnimationEngine _animationEngine = null!;
    private BorderRenderer _borderRenderer = null!;
    private TaskbarManager _taskbarManager = null!;

    // ──────────────── UI ────────────────
    private TaskbarIcon? _trayIcon;
    private readonly List<TopBarWindow> _topBarWindows = new();
    private SystemInfoService _sysInfoService = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // 1. Initialize logger
            Logger.Initialize();
            Logger.Instance.Info("═══════════════════════════════════════");
            Logger.Instance.Info("HyprWin starting...");

            // 1a. Refuse to run inside an RDP / Terminal Services session.
            //     Hooks and tiling would affect the wrong desktop context and
            //     keyboard shortcuts would interfere with the remote session.
            if (SessionHelper.IsRemoteSession())
            {
                Logger.Instance.Info("Remote Desktop session detected — HyprWin will not start.");
                MessageBox.Show(
                    "HyprWin does not run inside Remote Desktop (RDP) sessions.\n" +
                    "Please start HyprWin directly on the local machine.",
                    "HyprWin — RDP not supported",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown(0);
                return;
            }

            // 2. Load configuration (supports --config <path> argument)
            string? configPath = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--config", StringComparison.OrdinalIgnoreCase))
                {
                    configPath = args[i + 1];
                    Logger.Instance.Info($"Config path from command line: {configPath}");
                    break;
                }
            }
            _configManager = new ConfigManager(configPath);
            var config = _configManager.Load();
            Logger.Instance.Info($"Config loaded from: {_configManager.ConfigPath}");

            // 2b. Start hardware + audio monitoring service
            _sysInfoService = new SystemInfoService();
            _sysInfoService.Start();

            // 3. Initialize animation engine
            _animationEngine = new AnimationEngine();
            _animationEngine.UpdateFromConfig(config.Animations);

            // 4a. Hide native taskbar NOW — before enumerating monitors,
            //     so that Windows' work-area snapshot already reflects the
            //     hidden state when MonitorManager runs (belt-and-suspenders;
            //     MonitorManager uses rcMonitor anyway).
            _taskbarManager = new TaskbarManager();
            _taskbarManager.HideTaskbar();

            // 4b. Enumerate monitors
            _monitorManager = new MonitorManager();
            _monitorManager.Enumerate(
                config.TopBar.Enabled ? config.TopBar.Height : 0,
                config.TopBar.Position);

            // 5. Initialize window tracking
            _windowTracker = new WindowTracker();
            _windowTracker.SetExclusions(
                config.Exclude.ProcessNames,
                config.Exclude.ClassNames);

            // 6. Initialize workspace manager
            _workspaceManager = new WorkspaceManager(_monitorManager, _windowTracker);
            _workspaceManager.Initialize(config.General.WorkspaceCount);

            // 7. Initialize tiling engine
            _tilingEngine = new TilingEngine(_monitorManager, _animationEngine);
            _tilingEngine.UpdateLayout(config.Layout.GapsInner, config.Layout.GapsOuter);

            // 8. Initialize dispatcher
            _dispatcher = new WindowDispatcher(_windowTracker, _workspaceManager, _tilingEngine, _monitorManager);
            _dispatcher.SetTerminalCommand(config.General.TerminalCommand);
            _dispatcher.SetWorkspaceMode(config.General.WorkspaceMode);

            // 9. Install keyboard hook (must be on UI thread)
            _keyboardHook = new KeyboardHook();
            RegisterKeybinds(config);
            _keyboardHook.Install();

            // 10. Start window tracking & discover existing windows
            _windowTracker.Start(_monitorManager);
            _workspaceManager.AssignExistingWindows();

            // 11. Wire up workspace events
            _workspaceManager.RetileRequested += ws =>
            {
                _tilingEngine.RebuildTree(ws);
                _tilingEngine.TileWorkspace(ws);
            };

            // 12. Wire up window tracker events
            _windowTracker.WindowAdded += OnWindowAdded;
            _windowTracker.WindowRemoved += OnWindowRemoved;
            _windowTracker.FocusChanged += OnFocusChanged;
            _windowTracker.WindowRestored += OnWindowRestored;
            _windowTracker.WindowMinimized += OnWindowMinimized;

            // 14. Initial tile of all workspaces
            foreach (var mon in _monitorManager.Monitors)
            {
                var ws = _workspaceManager.GetActiveWorkspace(mon.Index);
                if (ws != null)
                {
                    _tilingEngine.RebuildTree(ws);
                    _tilingEngine.TileWorkspace(ws, animate: false);
                }
            }

            // 15. Start border renderer
            _borderRenderer = new BorderRenderer();
            _borderRenderer.UpdateTheme(
                config.Theme.BorderActive,
                config.Theme.BorderInactive,
                config.Layout.BorderSize,
                config.Layout.Rounding);
            _borderRenderer.Start();

            // 16. Create top bar windows
            if (config.TopBar.Enabled)
            {
                CreateTopBars(config);
            }

            // 17. Create tray icon
            CreateTrayIcon();

            // 18. Start config file watching
            _configManager.ConfigChanged += OnConfigChanged;
            _configManager.StartWatching();

            Logger.Instance.Info("HyprWin started successfully");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Fatal error during startup", ex);
            MessageBox.Show($"HyprWin failed to start:\n{ex.Message}", "HyprWin Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Instance.Info("HyprWin shutting down...");

        // Restore taskbar: kill Explorer and restart it so the taskbar & system
        // tray come back cleanly (SW_SHOW alone is unreliable after SW_HIDE).
        try { RestartExplorer(); } catch { }

        // Dispose TaskbarManager as well so rcWork is refreshed on the shell side
        try { _taskbarManager?.Dispose(); } catch { }

        try
        {
            _keyboardHook?.Dispose();

            // Restore all managed windows to their original positions
            _windowTracker?.RestoreAllWindows();

            _sysInfoService?.Dispose();
            _windowTracker?.Dispose();
            _borderRenderer?.Dispose();
            _animationEngine?.Dispose();
            _configManager?.Dispose();

            foreach (var bar in _topBarWindows)
                bar.Close();

            _trayIcon?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error during shutdown", ex);
        }

        Logger.Instance.Info("HyprWin stopped");
        Logger.Instance.Dispose();

        base.OnExit(e);
    }

    // ──────────────── Explorer Restart ────────────────

    /// <summary>
    /// Kills all explorer.exe instances and starts a fresh one.
    /// This is the most reliable way to restore the taskbar, system tray, and
    /// work-area reservation after HyprWin has hidden/modified the shell.
    /// </summary>
    private static void RestartExplorer()
    {
        try
        {
            Logger.Instance.Info("Restarting Explorer to restore taskbar...");

            // Kill all explorer instances
            foreach (var proc in Process.GetProcessesByName("explorer"))
            {
                try { proc.Kill(); proc.WaitForExit(3000); }
                catch { /* ignore — may already have exited */ }
                finally { proc.Dispose(); }
            }

            // Brief pause so Windows detects the crash and its auto-restart
            // doesn't interfere with our explicit restart below.
            System.Threading.Thread.Sleep(500);

            // Start a fresh explorer (shell mode)
            Process.Start(new ProcessStartInfo
            {
                FileName         = "explorer.exe",
                UseShellExecute  = true,
            });

            Logger.Instance.Info("Explorer restarted successfully");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to restart Explorer", ex);
        }
    }

    // ──────────────── Keybind Registration ────────────────

    private void RegisterKeybinds(HyprWinConfig config)
    {
        _keyboardHook.ClearRegistrations();
        _keyboardHook.RegisterFromConfig(config);

        var kb = config.Keybinds;

        // Focus
        _keyboardHook.RegisterKeybind(kb.FocusLeft, _dispatcher.FocusLeft);
        _keyboardHook.RegisterKeybind(kb.FocusRight, _dispatcher.FocusRight);
        _keyboardHook.RegisterKeybind(kb.FocusUp, _dispatcher.FocusUp);
        _keyboardHook.RegisterKeybind(kb.FocusDown, _dispatcher.FocusDown);

        // Move
        _keyboardHook.RegisterKeybind(kb.MoveLeft, _dispatcher.MoveLeft);
        _keyboardHook.RegisterKeybind(kb.MoveRight, _dispatcher.MoveRight);
        _keyboardHook.RegisterKeybind(kb.MoveUp, _dispatcher.MoveUp);
        _keyboardHook.RegisterKeybind(kb.MoveDown, _dispatcher.MoveDown);

        // Actions
        _keyboardHook.RegisterKeybind(kb.CloseWindow, _dispatcher.CloseWindow);
        _keyboardHook.RegisterKeybind(kb.ToggleFloat, _dispatcher.ToggleFloat);
        _keyboardHook.RegisterKeybind(kb.Fullscreen, _dispatcher.ToggleFullscreen);
        _keyboardHook.RegisterKeybind(kb.LaunchTerminal, _dispatcher.LaunchTerminal);
        _keyboardHook.RegisterKeybind(kb.LaunchExplorer, _dispatcher.LaunchExplorer);
        _keyboardHook.RegisterKeybind(kb.Screenshot, _dispatcher.TakeScreenshot);

        // Resize — registered as repeatable so holding the key continuously resizes
        _keyboardHook.RegisterRepeatableKeybind(kb.ResizeLeft, _dispatcher.ResizeLeft);
        _keyboardHook.RegisterRepeatableKeybind(kb.ResizeRight, _dispatcher.ResizeRight);
        _keyboardHook.RegisterRepeatableKeybind(kb.ResizeUp, _dispatcher.ResizeUp);
        _keyboardHook.RegisterRepeatableKeybind(kb.ResizeDown, _dispatcher.ResizeDown);

        // Workspaces
        _keyboardHook.RegisterKeybind(kb.Workspace1, () => _dispatcher.SwitchToWorkspace(0));
        _keyboardHook.RegisterKeybind(kb.Workspace2, () => _dispatcher.SwitchToWorkspace(1));
        _keyboardHook.RegisterKeybind(kb.Workspace3, () => _dispatcher.SwitchToWorkspace(2));

        // Move to workspace
        _keyboardHook.RegisterKeybind(kb.MoveToWs1, () => _dispatcher.MoveToWorkspace(0));
        _keyboardHook.RegisterKeybind(kb.MoveToWs2, () => _dispatcher.MoveToWorkspace(1));
        _keyboardHook.RegisterKeybind(kb.MoveToWs3, () => _dispatcher.MoveToWorkspace(2));

        Logger.Instance.Info("Keybinds registered");
    }

    // ──────────────── Event Handlers ────────────────

    private void OnWindowAdded(ManagedWindow window)
    {
        _workspaceManager.AddWindowToActiveWorkspace(window);
        Logger.Instance.Debug($"New window tiled: {window}");
    }

    private void OnWindowRemoved(IntPtr hwnd)
    {
        _workspaceManager.RemoveWindow(hwnd);
        Logger.Instance.Debug($"Window removed from tiling: {hwnd}");
    }

    private void OnWindowMinimized(IntPtr hwnd)
    {
        // A window was minimized — rebuild + retile so the blank leaf disappears.
        var ws = _workspaceManager.FindWorkspaceForWindow(hwnd);
        if (ws == null) return;
        _tilingEngine.RebuildTree(ws);
        _tilingEngine.TileWorkspace(ws, animate: false);
        Logger.Instance.Debug($"Retiled after window minimize: {hwnd}");
    }

    private void OnWindowRestored(IntPtr hwnd)
    {
        // A window was restored from minimized state. Retile its workspace so the
        // blank area left by the stale leaf is filled.
        var ws = _workspaceManager.FindWorkspaceForWindow(hwnd);
        if (ws == null) return;
        _tilingEngine.RebuildTree(ws);
        _tilingEngine.TileWorkspace(ws, animate: false);
        Logger.Instance.Debug($"Retiled after window restore: {hwnd}");
    }

    private void OnFocusChanged(IntPtr hwnd)
    {
        _workspaceManager.UpdateFocus(hwnd);
        _borderRenderer?.TrackWindow(hwnd);

        // Update workspace indicators on all top bars
        foreach (var bar in _topBarWindows)
        {
            bar.UpdateWorkspaceIndicators();
        }
    }

    private void OnConfigChanged(HyprWinConfig config)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                // Re-register keybinds
                RegisterKeybinds(config);

                // Update animation engine
                _animationEngine.UpdateFromConfig(config.Animations);

                // Update tiling layout
                _tilingEngine.UpdateLayout(config.Layout.GapsInner, config.Layout.GapsOuter);

                // Update border renderer
                _borderRenderer?.UpdateTheme(
                    config.Theme.BorderActive,
                    config.Theme.BorderInactive,
                    config.Layout.BorderSize,
                    config.Layout.Rounding);

                // Update dispatcher
                _dispatcher.SetTerminalCommand(config.General.TerminalCommand);
                _dispatcher.SetWorkspaceMode(config.General.WorkspaceMode);

                // Update exclusions
                _windowTracker?.SetExclusions(
                    config.Exclude.ProcessNames,
                    config.Exclude.ClassNames);

                // Update top bars
                foreach (var bar in _topBarWindows)
                    bar.ApplyConfig(config);

                // Retile all active workspaces
                foreach (var mon in _monitorManager.Monitors)
                {
                    var ws = _workspaceManager.GetActiveWorkspace(mon.Index);
                    if (ws != null)
                    {
                        _tilingEngine.RebuildTree(ws);
                        _tilingEngine.TileWorkspace(ws, animate: false);
                    }
                }

                // Show tray notification
                _trayIcon?.ShowNotification("HyprWin", "Config reloaded successfully",
                    H.NotifyIcon.Core.NotificationIcon.Info);

                Logger.Instance.Info("Config reload applied to all subsystems");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("Error applying config reload", ex);
            }
        });
    }

    // ──────────────── Top Bar ────────────────

    private void CreateTopBars(HyprWinConfig config)
    {
        foreach (var bar in _topBarWindows)
            bar.Close();
        _topBarWindows.Clear();

        foreach (var mon in _monitorManager.Monitors)
        {
            var bar = new TopBarWindow(mon, config, _workspaceManager, _sysInfoService);
            bar.SetConfigPath(_configManager.ConfigPath);
            _topBarWindows.Add(bar);
            bar.Show();
        }

        Logger.Instance.Info($"Created {_topBarWindows.Count} top bar(s)");
    }

    // ──────────────── Tray Icon ────────────────

    private void CreateTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "HyprWin — Hyprland for Windows",
            ContextMenu = CreateTrayMenu(),
        };

        // Load icon from the executable's embedded icon (set via ApplicationIcon in .csproj)
        try
        {
            var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null && System.IO.File.Exists(exePath))
            {
                _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Warn($"Could not load tray icon: {ex.Message}");
        }

        _trayIcon.ForceCreate();
        Logger.Instance.Info("Tray icon created");
    }

    private System.Windows.Controls.ContextMenu CreateTrayMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var reloadItem = new System.Windows.Controls.MenuItem { Header = "Reload Config" };
        reloadItem.Click += (_, _) =>
        {
            try
            {
                var config = _configManager.Load();
                OnConfigChanged(config);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("Manual config reload failed", ex);
            }
        };

        var openConfigItem = new System.Windows.Controls.MenuItem { Header = "Open Config" };
        openConfigItem.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _configManager.ConfigPath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("Failed to open config", ex);
            }
        };

        var openConfigFolderItem = new System.Windows.Controls.MenuItem { Header = "Open Config Folder" };
        openConfigFolderItem.Click += (_, _) =>
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_configManager.ConfigPath);
                if (dir != null)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true,
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("Failed to open config folder", ex);
            }
        };

        var openLogItem = new System.Windows.Controls.MenuItem { Header = "Open Log" };
        openLogItem.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Logger.GetDefaultLogPath(),
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("Failed to open log", ex);
            }
        };

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Shutdown();

        menu.Items.Add(reloadItem);
        menu.Items.Add(openConfigItem);
        menu.Items.Add(openConfigFolderItem);
        menu.Items.Add(openLogItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(exitItem);

        return menu;
    }
}
