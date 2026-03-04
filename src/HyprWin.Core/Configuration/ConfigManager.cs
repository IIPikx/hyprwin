using System.IO;
using Tomlyn;
using Tomlyn.Model;

namespace HyprWin.Core.Configuration;

/// <summary>
/// Manages loading, parsing, and live-reloading of hyprwin.toml configuration.
/// Config objects are immutable snapshots — a new instance is created on each reload.
/// </summary>
public sealed class ConfigManager : IDisposable
{
    private readonly string _configPath;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private volatile HyprWinConfig _current;
    private bool _disposed;

    public HyprWinConfig Current => _current;
    public string ConfigPath => _configPath;

    /// <summary>Fired when config is successfully reloaded. Passes the new config snapshot.</summary>
    public event Action<HyprWinConfig>? ConfigChanged;

    public ConfigManager(string? configPath = null)
    {
        _configPath = configPath ?? ResolveConfigPath();
        _current = new HyprWinConfig();
    }

    /// <summary>
    /// Resolve the config path in priority order:
    /// 1. Explicit path passed as argument
    /// 2. HYPRWIN_CONFIG environment variable
    /// 3. hyprwin.toml next to the executable
    /// 4. Default: %APPDATA%\HyprWin\hyprwin.toml
    /// </summary>
    private static string ResolveConfigPath()
    {
        // Check environment variable
        var envPath = Environment.GetEnvironmentVariable("HYPRWIN_CONFIG");
        if (!string.IsNullOrWhiteSpace(envPath) && (File.Exists(envPath) || Directory.Exists(Path.GetDirectoryName(envPath))))
        {
            Logger.Instance.Info($"Using config from HYPRWIN_CONFIG: {envPath}");
            return envPath;
        }

        // Check for config next to executable (portable mode)
        var exeDir = AppContext.BaseDirectory;
        var portablePath = Path.Combine(exeDir, "hyprwin.toml");
        if (File.Exists(portablePath))
        {
            Logger.Instance.Info($"Using portable config: {portablePath}");
            return portablePath;
        }

        // Default: %APPDATA%
        return GetDefaultConfigPath();
    }

    public static string GetDefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "HyprWin", "hyprwin.toml");
    }

    /// <summary>
    /// Load config from disk. If the file doesn't exist, write the default config first.
    /// </summary>
    public HyprWinConfig Load()
    {
        var dir = Path.GetDirectoryName(_configPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(_configPath))
        {
            Logger.Instance.Info($"Config file not found, generating default at: {_configPath}");
            File.WriteAllText(_configPath, DefaultConfig.Content, System.Text.Encoding.UTF8);
        }

        try
        {
            _current = ParseConfig(_configPath);
            Logger.Instance.Info("Configuration loaded successfully");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Config file has syntax errors, regenerating default config", ex);

            // Back up the broken config so the user doesn't lose custom values
            var backupPath = _configPath + ".broken";
            try { File.Copy(_configPath, backupPath, overwrite: true); } catch { }

            // Overwrite with clean default config
            File.WriteAllText(_configPath, DefaultConfig.Content, System.Text.Encoding.UTF8);
            _current = ParseConfig(_configPath);

            Logger.Instance.Info($"Default config restored. Broken config backed up to: {backupPath}");
        }

        return _current;
    }

    /// <summary>
    /// Start watching the config file for changes. Calls ConfigChanged on successful reload.
    /// </summary>
    public void StartWatching()
    {
        var dir = Path.GetDirectoryName(_configPath)!;
        var file = Path.GetFileName(_configPath);

        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

        _watcher = new FileSystemWatcher(dir)
        {
            Filter = file,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;

        Logger.Instance.Info($"Watching config file: {_configPath}");
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce — editors fire multiple change events per save
        _debounceTimer?.Change(300, Timeout.Infinite);
    }

    private void OnDebounceElapsed(object? state)
    {
        try
        {
            var newConfig = ParseConfig(_configPath);
            Interlocked.Exchange(ref _current, newConfig);
            Logger.Instance.Info("Configuration reloaded successfully");
            ConfigChanged?.Invoke(newConfig);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to reload config, keeping previous config", ex);
        }
    }

    /// <summary>
    /// Parse a TOML config file into a HyprWinConfig object.
    /// </summary>
    private static HyprWinConfig ParseConfig(string path)
    {
        // Retry a few times in case the file is still being written
        string toml = "";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                toml = File.ReadAllText(path);
                break;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
        }

        var table = Toml.ToModel(toml);
        var config = new HyprWinConfig
        {
            General = ParseGeneral(table),
            Keybinds = ParseKeybinds(table),
            WindowsKeysToSuppress = ParseSuppressedKeys(table),
            WindowsKeysToPassthrough = ParsePassthroughKeys(table),
            Animations = ParseAnimations(table),
            Layout = ParseLayout(table),
            Theme = ParseTheme(table),
            TopBar = ParseTopBar(table),
            Exclude = ParseExclude(table),
            Launch = ParseLaunchEntries(table),
            WindowRules = ParseWindowRules(table),
            Beziers = ParseBeziers(table),
        };

        return config;
    }

    private static GeneralConfig ParseGeneral(TomlTable table)
    {
        if (!table.TryGetValue("general", out var obj) || obj is not TomlTable t)
            return new GeneralConfig();

        return new GeneralConfig
        {
            WorkspaceCount = GetInt(t, "workspace_count", 3),
            TerminalCommand = GetString(t, "terminal_command", "wt.exe"),
            WorkspaceMode = GetString(t, "workspace_mode", "monitor_bound"),
            Autostart = GetBool(t, "autostart", false),
        };
    }

    private static KeybindsConfig ParseKeybinds(TomlTable table)
    {
        if (!table.TryGetValue("keybinds", out var obj) || obj is not TomlTable t)
            return new KeybindsConfig();

        return new KeybindsConfig
        {
            FocusLeft = GetString(t, "focus_left", "SUPER+LEFT"),
            FocusRight = GetString(t, "focus_right", "SUPER+RIGHT"),
            FocusUp = GetString(t, "focus_up", "SUPER+UP"),
            FocusDown = GetString(t, "focus_down", "SUPER+DOWN"),
            MoveLeft = GetString(t, "move_left", "SUPER+SHIFT+LEFT"),
            MoveRight = GetString(t, "move_right", "SUPER+SHIFT+RIGHT"),
            MoveUp = GetString(t, "move_up", "SUPER+SHIFT+UP"),
            MoveDown = GetString(t, "move_down", "SUPER+SHIFT+DOWN"),
            CloseWindow = GetString(t, "close_window", "SUPER+Q"),
            ToggleFloat = GetString(t, "toggle_float", "SUPER+T"),
            Fullscreen = GetString(t, "fullscreen", "SUPER+F"),
            Workspace1 = GetString(t, "workspace_1", "SUPER+1"),
            Workspace2 = GetString(t, "workspace_2", "SUPER+2"),
            Workspace3 = GetString(t, "workspace_3", "SUPER+3"),
            MoveToWs1 = GetString(t, "move_to_ws_1", "SUPER+SHIFT+1"),
            MoveToWs2 = GetString(t, "move_to_ws_2", "SUPER+SHIFT+2"),
            MoveToWs3 = GetString(t, "move_to_ws_3", "SUPER+SHIFT+3"),
            LaunchTerminal = GetString(t, "launch_terminal", "SUPER+RETURN"),
            LaunchExplorer = GetString(t, "launch_explorer", "SUPER+E"),
            ResizeLeft = GetString(t, "resize_left", "SUPER+CTRL+LEFT"),
            ResizeRight = GetString(t, "resize_right", "SUPER+CTRL+RIGHT"),
            ResizeUp = GetString(t, "resize_up", "SUPER+CTRL+UP"),
            ResizeDown = GetString(t, "resize_down", "SUPER+CTRL+DOWN"),
        };
    }

    private static WindowsKeysToSuppressConfig ParseSuppressedKeys(TomlTable table)
    {
        if (!table.TryGetValue("windows_keys_to_suppress", out var obj) || obj is not TomlTable t)
            return new WindowsKeysToSuppressConfig();

        if (t.TryGetValue("keys", out var keysObj) && keysObj is TomlArray arr)
        {
            var keys = new List<string>();
            foreach (var item in arr)
            {
                if (item is string s)
                    keys.Add(s);
            }
            return new WindowsKeysToSuppressConfig { Keys = keys };
        }

        return new WindowsKeysToSuppressConfig();
    }

    private static WindowsKeysToPassthroughConfig ParsePassthroughKeys(TomlTable table)
    {
        if (!table.TryGetValue("windows_keys_to_passthrough", out var obj) || obj is not TomlTable t)
            return new WindowsKeysToPassthroughConfig();

        if (t.TryGetValue("keys", out var keysObj) && keysObj is TomlArray arr)
        {
            var keys = new List<string>();
            foreach (var item in arr)
            {
                if (item is string s)
                    keys.Add(s);
            }
            return new WindowsKeysToPassthroughConfig { Keys = keys };
        }

        return new WindowsKeysToPassthroughConfig();
    }

    private static AnimationsConfig ParseAnimations(TomlTable table)
    {
        if (!table.TryGetValue("animations", out var obj) || obj is not TomlTable t)
            return new AnimationsConfig();

        return new AnimationsConfig
        {
            Enabled = GetBool(t, "enabled", true),
            WindowOpenDurationMs = GetInt(t, "window_open_duration_ms", 200),
            WindowCloseDurationMs = GetInt(t, "window_close_duration_ms", 150),
            WindowMoveDurationMs = GetInt(t, "window_move_duration_ms", 120),
            Easing = GetString(t, "easing", "ease_out_cubic"),
            WindowOpenStyle = GetString(t, "window_open_style", "popin"),
            PopinPercent = GetInt(t, "popin_percent", 80),
        };
    }

    private static LayoutConfig ParseLayout(TomlTable table)
    {
        if (!table.TryGetValue("layout", out var obj) || obj is not TomlTable t)
            return new LayoutConfig();

        return new LayoutConfig
        {
            GapsInner = GetInt(t, "gaps_inner", 8),
            GapsOuter = GetInt(t, "gaps_outer", 7),
            BorderSize = GetInt(t, "border_size", 2),
            Rounding = GetInt(t, "rounding", 8),
        };
    }

    private static ThemeConfig ParseTheme(TomlTable table)
    {
        if (!table.TryGetValue("theme", out var obj) || obj is not TomlTable t)
            return new ThemeConfig();

        return new ThemeConfig
        {
            BorderActive = GetString(t, "border_active", "#cba6f7"),
            BorderInactive = GetString(t, "border_inactive", "#45475a"),
            Background = GetString(t, "background", "#1e1e2e"),
            TopBarBg = GetString(t, "top_bar_bg", "#181825"),
            TopBarFg = GetString(t, "top_bar_fg", "#cdd6f4"),
            TopBarAccent = GetString(t, "top_bar_accent", "#89b4fa"),
        };
    }

    private static TopBarConfig ParseTopBar(TomlTable table)
    {
        if (!table.TryGetValue("top_bar", out var obj) || obj is not TomlTable t)
            return new TopBarConfig();

        return new TopBarConfig
        {
            Enabled = GetBool(t, "enabled", true),
            Height = GetInt(t, "height", 30),
            Position = GetString(t, "position", "top"),
            Font = GetString(t, "font", "JetBrainsMono Nerd Font"),
            FontSize = GetInt(t, "font_size", 12),
            ModulesLeft = ParseModules(t, "modules_left"),
            ModulesCenter = ParseModules(t, "modules_center"),
            ModulesRight = ParseModules(t, "modules_right"),
            Clock = ParseClock(t),
            Workspaces = ParseWorkspacesWidget(t),
        };
    }

    private static TopBarModulesConfig ParseModules(TomlTable parent, string key)
    {
        if (!parent.TryGetValue(key, out var obj) || obj is not TomlTable t)
        {
            return key switch
            {
                "modules_left" => new TopBarModulesConfig { Modules = new() { "workspaces" } },
                "modules_center" => new TopBarModulesConfig { Modules = new() { "clock" } },
                "modules_right" => new TopBarModulesConfig { Modules = new() { "tray", "cpu", "memory", "volume" } },
                _ => new TopBarModulesConfig()
            };
        }

        if (t.TryGetValue("modules", out var modsObj) && modsObj is TomlArray arr)
        {
            return new TopBarModulesConfig
            {
                Modules = arr.OfType<string>().ToList()
            };
        }

        return new TopBarModulesConfig();
    }

    private static ClockConfig ParseClock(TomlTable parent)
    {
        if (!parent.TryGetValue("clock", out var obj) || obj is not TomlTable t)
            return new ClockConfig();

        return new ClockConfig
        {
            Format = GetString(t, "format", "HH:mm:ss"),
            ShowDate = GetBool(t, "show_date", true),
            DateFormat = GetString(t, "date_format", "ddd dd.MM.yyyy"),
        };
    }

    private static WorkspacesWidgetConfig ParseWorkspacesWidget(TomlTable parent)
    {
        if (!parent.TryGetValue("workspaces", out var obj) || obj is not TomlTable t)
            return new WorkspacesWidgetConfig();

        return new WorkspacesWidgetConfig
        {
            ShowCount = GetInt(t, "show_count", 3),
            ActiveIndicator = GetString(t, "active_indicator", "●"),
            InactiveIndicator = GetString(t, "inactive_indicator", "○"),
        };
    }

    // ──────────────── TOML helpers ────────────────

    private static List<LaunchEntry> ParseLaunchEntries(TomlTable table)
    {
        var entries = new List<LaunchEntry>();
        if (!table.TryGetValue("launch", out var obj))
            return entries;

        // [[launch]] is an array of tables in TOML
        if (obj is TomlTableArray tableArray)
        {
            foreach (var t in tableArray)
            {
                var shortcut = GetString(t, "shortcut", "");
                var command = GetString(t, "command", "");
                if (string.IsNullOrWhiteSpace(shortcut) || string.IsNullOrWhiteSpace(command))
                    continue;

                entries.Add(new LaunchEntry
                {
                    Shortcut = shortcut,
                    Command = command,
                    Args = GetString(t, "args", ""),
                });
            }
            Logger.Instance.Info($"Parsed {entries.Count} custom launch shortcut(s)");
        }

        return entries;
    }

    private static List<WindowRuleConfig> ParseWindowRules(TomlTable table)
    {
        var rules = new List<WindowRuleConfig>();
        if (!table.TryGetValue("window_rule", out var obj))
            return rules;

        if (obj is TomlTableArray tableArray)
        {
            foreach (var t in tableArray)
            {
                // At least one match criterion is required
                var matchProcess = GetStringOrNull(t, "match_process");
                var matchClass = GetStringOrNull(t, "match_class");
                var matchTitle = GetStringOrNull(t, "match_title");
                if (matchProcess == null && matchClass == null && matchTitle == null)
                    continue;

                rules.Add(new WindowRuleConfig
                {
                    MatchProcess = matchProcess,
                    MatchClass = matchClass,
                    MatchTitle = matchTitle,
                    Float = GetBoolOrNull(t, "float"),
                    Fullscreen = GetBoolOrNull(t, "fullscreen"),
                    Workspace = GetIntOrNull(t, "workspace"),
                    Pin = GetBoolOrNull(t, "pin"),
                    Center = GetBoolOrNull(t, "center"),
                    NoAnim = GetBoolOrNull(t, "no_anim"),
                    Opacity = GetDoubleOrNull(t, "opacity"),
                    BorderColor = GetStringOrNull(t, "border_color"),
                    BorderSize = GetIntOrNull(t, "border_size"),
                    Size = GetStringOrNull(t, "size"),
                    Move = GetStringOrNull(t, "move"),
                });
            }
            Logger.Instance.Info($"Parsed {rules.Count} window rule(s)");
        }

        return rules;
    }

    private static List<BezierConfig> ParseBeziers(TomlTable table)
    {
        var beziers = new List<BezierConfig>();
        if (!table.TryGetValue("bezier", out var obj))
            return beziers;

        if (obj is TomlTableArray tableArray)
        {
            foreach (var t in tableArray)
            {
                var name = GetString(t, "name", "");
                if (string.IsNullOrWhiteSpace(name)) continue;

                beziers.Add(new BezierConfig
                {
                    Name = name,
                    X0 = GetDouble(t, "x0", 0.0),
                    Y0 = GetDouble(t, "y0", 0.0),
                    X1 = GetDouble(t, "x1", 1.0),
                    Y1 = GetDouble(t, "y1", 1.0),
                });
            }
            Logger.Instance.Info($"Parsed {beziers.Count} bezier curve(s)");
        }

        return beziers;
    }

    private static ExcludeConfig ParseExclude(TomlTable table)
    {
        if (!table.TryGetValue("exclude", out var obj) || obj is not TomlTable t)
            return new ExcludeConfig();

        var processNames = new List<string>();
        var classNames = new List<string>();

        if (t.TryGetValue("process_names", out var pObj) && pObj is TomlArray pArr)
            processNames = pArr.OfType<string>().ToList();

        if (t.TryGetValue("class_names", out var cObj) && cObj is TomlArray cArr)
            classNames = cArr.OfType<string>().ToList();

        return new ExcludeConfig
        {
            ProcessNames = processNames,
            ClassNames = classNames,
        };
    }

    private static string GetString(TomlTable t, string key, string defaultValue)
    {
        if (t.TryGetValue(key, out var v) && v is string s)
            return s;
        return defaultValue;
    }

    private static string? GetStringOrNull(TomlTable t, string key)
    {
        if (t.TryGetValue(key, out var v) && v is string s && !string.IsNullOrWhiteSpace(s))
            return s;
        return null;
    }

    private static int GetInt(TomlTable t, string key, int defaultValue)
    {
        if (t.TryGetValue(key, out var v))
        {
            if (v is long l) return (int)l;
            if (v is int i) return i;
        }
        return defaultValue;
    }

    private static int? GetIntOrNull(TomlTable t, string key)
    {
        if (t.TryGetValue(key, out var v))
        {
            if (v is long l) return (int)l;
            if (v is int i) return i;
        }
        return null;
    }

    private static double GetDouble(TomlTable t, string key, double defaultValue)
    {
        if (t.TryGetValue(key, out var v))
        {
            if (v is double d) return d;
            if (v is long l) return l;
            if (v is int i) return i;
        }
        return defaultValue;
    }

    private static double? GetDoubleOrNull(TomlTable t, string key)
    {
        if (t.TryGetValue(key, out var v))
        {
            if (v is double d) return d;
            if (v is long l) return l;
        }
        return null;
    }

    private static bool GetBool(TomlTable t, string key, bool defaultValue)
    {
        if (t.TryGetValue(key, out var v) && v is bool b)
            return b;
        return defaultValue;
    }

    private static bool? GetBoolOrNull(TomlTable t, string key)
    {
        if (t.TryGetValue(key, out var v) && v is bool b)
            return b;
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
    }
}
