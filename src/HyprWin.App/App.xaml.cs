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
    private WindowRuleEngine _windowRuleEngine = null!;

    // ──────────────── UI ────────────────
    private TaskbarIcon? _trayIcon;
    private readonly List<TopBarWindow> _topBarWindows = new();
    private SystemInfoService _sysInfoService = null!;
    private TrayIconService _trayIconService = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Global exception handlers ──────────────────────────────────
        // Catch unhandled exceptions on the Dispatcher thread and background
        // threads to prevent 0xc000041d (STATUS_FATAL_USER_CALLBACK_EXCEPTION)
        // crashes, especially in VM environments where hardware APIs may fail.
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Instance.Error("Unhandled dispatcher exception (suppressed)", args.Exception);
            args.Handled = true; // prevent crash
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Logger.Instance.Error($"Unhandled AppDomain exception (IsTerminating={args.IsTerminating})", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Instance.Error("Unobserved task exception (suppressed)", args.Exception);
            args.SetObserved(); // prevent finalizer crash
        };

        try
        {
            // 1. Initialize logger
            Logger.Initialize();
            Logger.Instance.Info("═══════════════════════════════════════");
            Logger.Instance.Info("HyprWin starting...");

            // 1b. Disable ForegroundLockTimeout (Komorebi pattern).
            // Windows blocks SetForegroundWindow after a timeout if the calling
            // process is not the foreground process. Setting this to 0 on startup
            // ensures keyboard-driven focus changes always work reliably.
            try
            {
                HyprWin.Core.Interop.NativeMethods.DisableForegroundLockTimeout();
                Logger.Instance.Info("ForegroundLockTimeout disabled for reliable focus switching");
            }
            catch (Exception ex)
            {
                Logger.Instance.Warn($"Could not disable ForegroundLockTimeout: {ex.Message}");
            }

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
            try
            {
                _sysInfoService.Start();
            }
            catch (Exception ex)
            {
                Logger.Instance.Warn($"SystemInfoService failed to start (VM?): {ex.Message}");
                // App continues — top bar will show 0 values for temps/GPU
            }

            // 3. Initialize animation engine
            _animationEngine = new AnimationEngine();
            _animationEngine.UpdateFromConfig(config.Animations);

            // 3b. Register named bezier curves from config
            RegisterBeziers(config);

            // 3c. Initialize window rule engine
            _windowRuleEngine = new WindowRuleEngine();
            LoadWindowRules(config);

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
                // Hyprland dwindle behavior: sync the BSP tree incrementally
                // instead of rebuilding from scratch. This preserves split ratios
                // and tree structure across window adds/removes.
                _tilingEngine.SyncTree(ws);
                _tilingEngine.TileWorkspace(ws);
            };

            // 12. Wire up window tracker events
            _windowTracker.WindowAdded += OnWindowAdded;
            _windowTracker.WindowRemoved += OnWindowRemoved;
            _windowTracker.FocusChanged += OnFocusChanged;
            _windowTracker.WindowRestored += OnWindowRestored;
            _windowTracker.WindowMinimized += OnWindowMinimized;

            // 14. Initial tile of all workspaces (use SyncTree for dwindle-style layout)
            foreach (var mon in _monitorManager.Monitors)
            {
                var ws = _workspaceManager.GetActiveWorkspace(mon.Index);
                if (ws != null)
                {
                    _tilingEngine.SyncTree(ws);
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
                // 16a. Start system tray icon service (reads notification icons from hidden taskbar)
                _trayIconService = new TrayIconService();
                _trayIconService.Start();

                CreateTopBars(config);
            }

            // 17. Create tray icon
            CreateTrayIcon();

            // 18. Sync autostart registry entry with config
            AutostartManager.SetEnabled(config.General.Autostart);

            // 19. Start config file watching
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

        try
        {
            // 1. Stop keyboard hook first (release all hotkeys)
            _keyboardHook?.Dispose();

            // 2. Stop border renderer
            _borderRenderer?.Dispose();

            // 3. Restore all managed windows to their original positions & size
            _windowTracker?.RestoreAllWindows();

            // 4. Restore native taskbar (SW_SHOW + WM_SETTINGCHANGE, no Explorer restart)
            try { _taskbarManager?.Dispose(); } catch { }

            // 5. Clean up remaining services
            _sysInfoService?.Dispose();
            _trayIconService?.Dispose();
            _windowTracker?.Dispose();
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

        // Task Manager — explicit keybind because the LL hook interferes with native Ctrl+Shift+Esc
        _keyboardHook.RegisterKeybind(kb.LaunchTaskmgr, _dispatcher.LaunchTaskManager);

        // Settings & Color Picker
        _keyboardHook.RegisterKeybind(kb.LaunchSettings, () => _dispatcher.LaunchProgram("ms-settings:"));
        _keyboardHook.RegisterKeybind(kb.LaunchColorPicker, () => _dispatcher.LaunchProgram("PowerToys.ColorPickerUI.exe"));

        // Workspaces
        _keyboardHook.RegisterKeybind(kb.Workspace1, () => _dispatcher.SwitchToWorkspace(0));
        _keyboardHook.RegisterKeybind(kb.Workspace2, () => _dispatcher.SwitchToWorkspace(1));
        _keyboardHook.RegisterKeybind(kb.Workspace3, () => _dispatcher.SwitchToWorkspace(2));

        // Move to workspace
        _keyboardHook.RegisterKeybind(kb.MoveToWs1, () => _dispatcher.MoveToWorkspace(0));
        _keyboardHook.RegisterKeybind(kb.MoveToWs2, () => _dispatcher.MoveToWorkspace(1));
        _keyboardHook.RegisterKeybind(kb.MoveToWs3, () => _dispatcher.MoveToWorkspace(2));

        // Custom launch shortcuts from [[launch]] entries
        foreach (var entry in config.Launch)
        {
            var cmd = entry.Command;
            var args = entry.Args;
            _keyboardHook.RegisterKeybind(entry.Shortcut, () => _dispatcher.LaunchProgram(cmd, args));
            Logger.Instance.Debug($"Registered custom launch: {entry.Shortcut} → {cmd} {args}".TrimEnd());
        }

        Logger.Instance.Info("Keybinds registered");
    }

    // ──────────────── Event Handlers ────────────────

    private void OnWindowAdded(ManagedWindow window)
    {
        _workspaceManager.AddWindowToActiveWorkspace(window);

        // Apply window rules (Hyprland windowrule)
        if (_windowRuleEngine.HasRules)
        {
            var result = _windowRuleEngine.Evaluate(window);
            if (result.HasAnyEffect)
                ApplyWindowRuleResult(window, result);
        }

        Logger.Instance.Debug($"New window tiled: {window}");
    }

    private void OnWindowRemoved(IntPtr hwnd)
    {
        _workspaceManager.RemoveWindow(hwnd);
        Logger.Instance.Debug($"Window removed from tiling: {hwnd}");
    }

    private void OnWindowMinimized(IntPtr hwnd)
    {
        // A window was minimized — sync tree so the blank leaf disappears
        // while preserving all other split ratios and directions.
        var ws = _workspaceManager.FindWorkspaceForWindow(hwnd);
        if (ws == null) return;
        _tilingEngine.SyncTree(ws);
        _tilingEngine.TileWorkspace(ws, animate: false);
        Logger.Instance.Debug($"Retiled after window minimize: {hwnd}");
    }

    private void OnWindowRestored(IntPtr hwnd)
    {
        // A window was restored from minimized — sync tree so it's re-added
        // by splitting the focused window (Hyprland dwindle behavior).
        var ws = _workspaceManager.FindWorkspaceForWindow(hwnd);
        if (ws == null) return;
        _tilingEngine.SyncTree(ws);
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

                // Reload bezier curves and window rules
                RegisterBeziers(config);
                LoadWindowRules(config);

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

                // Sync autostart registry entry
                AutostartManager.SetEnabled(config.General.Autostart);

                // Update top bars
                foreach (var bar in _topBarWindows)
                    bar.ApplyConfig(config);

                // Retile all active workspaces (use SyncTree to preserve tree structure)
                foreach (var mon in _monitorManager.Monitors)
                {
                    var ws = _workspaceManager.GetActiveWorkspace(mon.Index);
                    if (ws != null)
                    {
                        _tilingEngine.SyncTree(ws);
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
            var bar = new TopBarWindow(mon, config, _workspaceManager, _sysInfoService, _trayIconService);
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

        var autostartItem = new System.Windows.Controls.MenuItem
        {
            Header = "Autostart",
            IsCheckable = true,
            IsChecked = AutostartManager.IsEnabled(),
        };
        autostartItem.Click += (_, _) =>
        {
            bool newState = autostartItem.IsChecked;
            AutostartManager.SetEnabled(newState);
            Logger.Instance.Info($"Autostart toggled to {newState} via tray menu");
        };

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Shutdown();

        menu.Items.Add(reloadItem);
        menu.Items.Add(openConfigItem);
        menu.Items.Add(openConfigFolderItem);
        menu.Items.Add(openLogItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(autostartItem);
        menu.Items.Add(exitItem);

        return menu;
    }

    // ──────────────── Bezier Curves & Window Rules ────────────────

    /// <summary>
    /// Register named bezier curves from config into the global Easing registry.
    /// </summary>
    private static void RegisterBeziers(HyprWinConfig config)
    {
        Easing.ClearBeziers();
        foreach (var b in config.Beziers)
        {
            if (string.IsNullOrWhiteSpace(b.Name)) continue;
            Easing.RegisterBezier(b.Name, b.X0, b.Y0, b.X1, b.Y1);
        }
    }

    /// <summary>
    /// Convert config window rules into WindowRule objects and load them into the engine.
    /// </summary>
    private void LoadWindowRules(HyprWinConfig config)
    {
        var rules = new List<WindowRule>();
        foreach (var rc in config.WindowRules)
        {
            try
            {
                var rule = new WindowRule
                {
                    MatchProcess = ToRegex(rc.MatchProcess),
                    MatchClass = ToRegex(rc.MatchClass),
                    MatchTitle = ToRegex(rc.MatchTitle),
                    Float = rc.Float,
                    Fullscreen = rc.Fullscreen,
                    Workspace = rc.Workspace,
                    Pin = rc.Pin,
                    Center = rc.Center,
                    NoAnim = rc.NoAnim,
                    Opacity = rc.Opacity,
                    BorderColor = rc.BorderColor,
                    BorderSize = rc.BorderSize,
                    Size = ParsePair(rc.Size),
                    Move = ParsePair(rc.Move),
                };
                rules.Add(rule);
            }
            catch (Exception ex)
            {
                Logger.Instance.Warn($"Invalid window rule: {ex.Message}");
            }
        }
        _windowRuleEngine.SetRules(rules);
    }

    /// <summary>
    /// Apply evaluated window rule effects to a managed window.
    /// </summary>
    private void ApplyWindowRuleResult(ManagedWindow window, WindowRuleResult result)
    {
        try
        {
            // Opacity (via WS_EX_LAYERED + SetLayeredWindowAttributes)
            if (result.Opacity.HasValue)
            {
                byte alpha = (byte)Math.Clamp((int)(result.Opacity.Value * 255), 0, 255);
                int exStyle = HyprWin.Core.Interop.NativeMethods.GetWindowLong(
                    window.Handle, HyprWin.Core.Interop.NativeMethods.GWL_EXSTYLE);
                HyprWin.Core.Interop.NativeMethods.SetWindowLong(
                    window.Handle, HyprWin.Core.Interop.NativeMethods.GWL_EXSTYLE,
                    exStyle | (int)HyprWin.Core.Interop.NativeMethods.WS_EX_LAYERED);
                HyprWin.Core.Interop.NativeMethods.SetLayeredWindowAttributes(
                    window.Handle, 0, alpha, HyprWin.Core.Interop.NativeMethods.LWA_ALPHA);
                Logger.Instance.Debug($"Window rule: opacity {result.Opacity.Value:F2} applied to {window.ProcessName}");
            }

            // Size
            if (result.Size.HasValue)
            {
                var (w, h) = result.Size.Value;
                HyprWin.Core.Interop.NativeMethods.SetWindowPos(
                    window.Handle, IntPtr.Zero, 0, 0, w, h,
                    HyprWin.Core.Interop.NativeMethods.SWP_NOMOVE |
                    HyprWin.Core.Interop.NativeMethods.SWP_NOZORDER);
            }

            // Move
            if (result.Move.HasValue)
            {
                var (x, y) = result.Move.Value;
                HyprWin.Core.Interop.NativeMethods.SetWindowPos(
                    window.Handle, IntPtr.Zero, x, y, 0, 0,
                    HyprWin.Core.Interop.NativeMethods.SWP_NOSIZE |
                    HyprWin.Core.Interop.NativeMethods.SWP_NOZORDER);
            }

            // Float (remove from tiling — Hyprland togglefloating)
            if (result.Float == true)
            {
                _dispatcher.ToggleFloat();
            }

            // Workspace assignment
            if (result.Workspace.HasValue)
            {
                int wsIndex = result.Workspace.Value - 1; // user-facing 1-based → 0-based
                if (wsIndex >= 0)
                    _dispatcher.MoveToWorkspace(wsIndex);
            }

            // Center on screen
            if (result.Center == true)
            {
                HyprWin.Core.Interop.NativeMethods.GetWindowRect(window.Handle, out var rect);
                int ww = rect.Right - rect.Left;
                int wh = rect.Bottom - rect.Top;
                var mon = _monitorManager.Monitors.FirstOrDefault();
                if (mon != null)
                {
                    int cx = mon.WorkArea.Left + (mon.WorkArea.Right - mon.WorkArea.Left - ww) / 2;
                    int cy = mon.WorkArea.Top + (mon.WorkArea.Bottom - mon.WorkArea.Top - wh) / 2;
                    HyprWin.Core.Interop.NativeMethods.SetWindowPos(
                        window.Handle, IntPtr.Zero, cx, cy, 0, 0,
                        HyprWin.Core.Interop.NativeMethods.SWP_NOSIZE |
                        HyprWin.Core.Interop.NativeMethods.SWP_NOZORDER);
                }
            }

            // Fullscreen
            if (result.Fullscreen == true)
            {
                _dispatcher.ToggleFullscreen();
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Warn($"Error applying window rule to {window.ProcessName}: {ex.Message}");
        }
    }

    private static System.Text.RegularExpressions.Regex? ToRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        return new System.Text.RegularExpressions.Regex(pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);
    }

    private static (int, int)? ParsePair(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split('x', 'X', ',', ' ');
        if (parts.Length >= 2 &&
            int.TryParse(parts[0].Trim(), out int a) &&
            int.TryParse(parts[1].Trim(), out int b))
            return (a, b);
        return null;
    }
}
