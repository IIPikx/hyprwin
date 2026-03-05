namespace HyprWin.Core.Configuration;

/// <summary>
/// Strongly-typed configuration model for HyprWin. Deserialized from hyprwin.toml.
/// All config objects are immutable snapshots — a new instance is created on each reload.
/// </summary>
public sealed class HyprWinConfig
{
    public KeybindsConfig Keybinds { get; init; } = new();
    public WindowsKeysToSuppressConfig WindowsKeysToSuppress { get; init; } = new();
    public WindowsKeysToPassthroughConfig WindowsKeysToPassthrough { get; init; } = new();
    public AnimationsConfig Animations { get; init; } = new();
    public LayoutConfig Layout { get; init; } = new();
    public ThemeConfig Theme { get; init; } = new();
    public TopBarConfig TopBar { get; init; } = new();
    public GeneralConfig General { get; init; } = new();
    public ExcludeConfig Exclude { get; init; } = new();

    /// <summary>Custom program launch shortcuts defined via [[launch]] entries.</summary>
    public List<LaunchEntry> Launch { get; init; } = new();

    /// <summary>Hyprland-style window rules defined via [[window_rule]] entries.</summary>
    public List<WindowRuleConfig> WindowRules { get; init; } = new();

    /// <summary>Named bezier curves defined via [[bezier]] entries.</summary>
    public List<BezierConfig> Beziers { get; init; } = new();
}

public sealed class GeneralConfig
{
    public int WorkspaceCount { get; init; } = 3;
    public string TerminalCommand { get; init; } = "wt.exe";
    /// <summary>
    /// Workspace mode: "monitor_bound" (Workspace N = Monitor N, default) or "virtual" (per-monitor virtual desktops).
    /// </summary>
    public string WorkspaceMode { get; init; } = "monitor_bound";
    /// <summary>
    /// Whether HyprWin should start automatically with Windows (registry Run key).
    /// </summary>
    public bool Autostart { get; init; } = false;
}

public sealed class KeybindsConfig
{
    public string FocusLeft { get; init; } = "SUPER+LEFT";
    public string FocusRight { get; init; } = "SUPER+RIGHT";
    public string FocusUp { get; init; } = "SUPER+UP";
    public string FocusDown { get; init; } = "SUPER+DOWN";
    public string MoveLeft { get; init; } = "SUPER+SHIFT+LEFT";
    public string MoveRight { get; init; } = "SUPER+SHIFT+RIGHT";
    public string MoveUp { get; init; } = "SUPER+SHIFT+UP";
    public string MoveDown { get; init; } = "SUPER+SHIFT+DOWN";
    public string CloseWindow { get; init; } = "SUPER+Q";
    public string ToggleFloat { get; init; } = "SUPER+T";
    public string Fullscreen { get; init; } = "SUPER+F";
    public string Workspace1 { get; init; } = "SUPER+1";
    public string Workspace2 { get; init; } = "SUPER+2";
    public string Workspace3 { get; init; } = "SUPER+3";
    public string MoveToWs1 { get; init; } = "SUPER+SHIFT+1";
    public string MoveToWs2 { get; init; } = "SUPER+SHIFT+2";
    public string MoveToWs3 { get; init; } = "SUPER+SHIFT+3";
    public string LaunchTerminal { get; init; } = "SUPER+RETURN";
    public string LaunchExplorer { get; init; } = "SUPER+E";
    public string Screenshot     { get; init; } = "SUPER+SHIFT+S";
    public string ResizeLeft { get; init; } = "SUPER+CTRL+LEFT";
    public string ResizeRight { get; init; } = "SUPER+CTRL+RIGHT";
    public string ResizeUp { get; init; } = "SUPER+CTRL+UP";
    public string ResizeDown { get; init; } = "SUPER+CTRL+DOWN";
    public string LaunchTaskmgr { get; init; } = "CTRL+SHIFT+ESCAPE";
    public string LaunchSettings { get; init; } = "SUPER+I";
    public string LaunchColorPicker { get; init; } = "SUPER+SHIFT+C";
}

public sealed class WindowsKeysToSuppressConfig
{
    public List<string> Keys { get; init; } = new()
    {
        "WIN+LEFT", "WIN+RIGHT", "WIN+UP", "WIN+DOWN",
        "WIN+D", "WIN+TAB",
        "WIN+1", "WIN+2", "WIN+3"
    };
}

/// <summary>
/// Windows key combos that should be passed through to the system.
/// Since HyprWin suppresses the Win key, these combos are re-injected
/// via keybd_event so the OS and other apps (like Raycast) can handle them.
/// </summary>
public sealed class WindowsKeysToPassthroughConfig
{
    public List<string> Keys { get; init; } = new()
    {
        "WIN+R", "WIN+SPACE"
    };
}

public sealed class AnimationsConfig
{
    public bool Enabled { get; init; } = true;
    public int WindowOpenDurationMs { get; init; } = 200;
    public int WindowCloseDurationMs { get; init; } = 150;
    public int WindowMoveDurationMs { get; init; } = 120;
    public string Easing { get; init; } = "ease_out_cubic";
    /// <summary>Animation style for window open. Options: slide, popin, fade.</summary>
    public string WindowOpenStyle { get; init; } = "popin";
    /// <summary>Percentage for popin style (start scale). 0-100.</summary>
    public int PopinPercent { get; init; } = 80;
}

public sealed class LayoutConfig
{
    public int GapsInner { get; init; } = 4;
    public int GapsOuter { get; init; } = 0;
    public int BorderSize { get; init; } = 2;
    public int Rounding { get; init; } = 8;
}

public sealed class ThemeConfig
{
    public string BorderActive { get; init; } = "#cba6f7";
    public string BorderInactive { get; init; } = "#45475a";
    public string Background { get; init; } = "#1e1e2e";
    public string TopBarBg { get; init; } = "#181825";
    public string TopBarFg { get; init; } = "#cdd6f4";
    public string TopBarAccent { get; init; } = "#89b4fa";
}

public sealed class TopBarConfig
{
    public bool Enabled { get; init; } = true;
    public int Height { get; init; } = 30;
    public string Position { get; init; } = "top";
    public string Font { get; init; } = "JetBrainsMono Nerd Font";
    public int FontSize { get; init; } = 12;
    public TopBarModulesConfig ModulesLeft { get; init; } = new() { Modules = new() { "workspaces" } };
    public TopBarModulesConfig ModulesCenter { get; init; } = new() { Modules = new() { "clock" } };
    public TopBarModulesConfig ModulesRight { get; init; } = new() { Modules = new() { "tray", "cpu", "cpu_temp", "gpu", "gpu_temp", "memory", "volume" } };
    public ClockConfig Clock { get; init; } = new();
    public WorkspacesWidgetConfig Workspaces { get; init; } = new();
}

public sealed class TopBarModulesConfig
{
    public List<string> Modules { get; init; } = new();
}

public sealed class ClockConfig
{
    public string Format { get; init; } = "HH:mm:ss";
    public bool ShowDate { get; init; } = true;
    public string DateFormat { get; init; } = "ddd dd.MM.yyyy";
}

public sealed class WorkspacesWidgetConfig
{
    public int ShowCount { get; init; } = 3;
    public string ActiveIndicator { get; init; } = "●";
    public string InactiveIndicator { get; init; } = "○";
}

/// <summary>
/// A single Hyprland-style window rule entry.
/// Configured via [[window_rule]] array-of-tables in TOML.
/// </summary>
public sealed class WindowRuleConfig
{
    /// <summary>Match by process name regex.</summary>
    public string? MatchProcess { get; init; }
    /// <summary>Match by window class regex.</summary>
    public string? MatchClass { get; init; }
    /// <summary>Match by window title regex.</summary>
    public string? MatchTitle { get; init; }

    // Effects
    public bool? Float { get; init; }
    public bool? Fullscreen { get; init; }
    public int? Workspace { get; init; }
    public bool? Pin { get; init; }
    public bool? Center { get; init; }
    public bool? NoAnim { get; init; }
    public double? Opacity { get; init; }
    public string? BorderColor { get; init; }
    public int? BorderSize { get; init; }
    public string? Size { get; init; }
    public string? Move { get; init; }
}

/// <summary>
/// A named bezier curve for animations.
/// Configured via [[bezier]] in TOML: name, x0, y0, x1, y1
/// </summary>
public sealed class BezierConfig
{
    public string Name { get; init; } = "";
    public double X0 { get; init; }
    public double Y0 { get; init; }
    public double X1 { get; init; }
    public double Y1 { get; init; }
}

/// <summary>
/// A single custom launch shortcut entry.
/// Configured via [[launch]] array-of-tables in TOML.
/// </summary>
public sealed class LaunchEntry
{
    /// <summary>Keyboard shortcut, e.g. "SUPER+B" or "CTRL+ALT+T".</summary>
    public string Shortcut { get; init; } = "";

    /// <summary>Command / executable to launch, e.g. "notepad.exe" or "C:\\Program Files\\App\\app.exe".</summary>
    public string Command { get; init; } = "";

    /// <summary>Optional arguments to pass to the command.</summary>
    public string Args { get; init; } = "";
}

/// <summary>
/// Programs to exclude from tiling/management.
/// </summary>
 public sealed class ExcludeConfig
{
    /// <summary>Process names (without .exe) to exclude from management.</summary>
    public List<string> ProcessNames { get; init; } = new() { "Taskmgr", "3CXDesktopApp", "mstsc", "msrdc" };

    /// <summary>Window class names to exclude from management.</summary>
    public List<string> ClassNames { get; init; } = new();
}
