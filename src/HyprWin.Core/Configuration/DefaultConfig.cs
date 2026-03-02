namespace HyprWin.Core.Configuration;

/// <summary>
/// Default hyprwin.toml content with inline comments. Written on first run.
/// </summary>
public static class DefaultConfig
{
    public const string Content = """
# ╔══════════════════════════════════════════════════════════════╗
# ║                    HyprWin Configuration                     ║
# ║        Hyprland-inspired window management for Windows 11    ║
# ╚══════════════════════════════════════════════════════════════╝
#
# This file is auto-reloaded when saved. No restart needed.
# Location: %APPDATA%\HyprWin\hyprwin.toml

# ─────────────────────── General ───────────────────────
[general]
workspace_count  = 3           # Number of virtual workspaces per monitor
terminal_command = "wt.exe"    # Command to launch terminal (SUPER+RETURN)
workspace_mode   = "monitor_bound"  # "monitor_bound" = Workspace N maps to Monitor N
                                     # "virtual" = per-monitor virtual desktops (hide/show)

# ─────────────────────── Keybinds ───────────────────────
# Format: "MODIFIER+KEY" where modifiers are SUPER, SHIFT, CTRL, ALT
# Keys: A-Z, 0-9, F1-F12, LEFT, RIGHT, UP, DOWN, RETURN, TAB, SPACE, etc.
[keybinds]
focus_left      = "SUPER+LEFT"
focus_right     = "SUPER+RIGHT"
focus_up        = "SUPER+UP"
focus_down      = "SUPER+DOWN"
move_left       = "SUPER+SHIFT+LEFT"
move_right      = "SUPER+SHIFT+RIGHT"
move_up         = "SUPER+SHIFT+UP"
move_down       = "SUPER+SHIFT+DOWN"
close_window    = "SUPER+Q"
toggle_float    = "SUPER+T"
fullscreen      = "SUPER+F"
workspace_1     = "SUPER+1"
workspace_2     = "SUPER+2"
workspace_3     = "SUPER+3"
move_to_ws_1    = "SUPER+SHIFT+1"
move_to_ws_2    = "SUPER+SHIFT+2"
move_to_ws_3    = "SUPER+SHIFT+3"
launch_terminal = "SUPER+RETURN"
launch_explorer = "SUPER+E"
screenshot      = "SUPER+SHIFT+S"
resize_left    = "SUPER+CTRL+LEFT"
resize_right   = "SUPER+CTRL+RIGHT"
resize_up      = "SUPER+CTRL+UP"
resize_down    = "SUPER+CTRL+DOWN"
launch_taskmgr = "CTRL+SHIFT+ESCAPE"  # Task Manager (explicit — LL hook blocks native shortcut)

# ─────────────────────── Keys to Suppress ───────────────────────
# Native Windows shortcuts to intercept and suppress.
# Note: WIN+L (lock) and Ctrl+Alt+Del cannot be suppressed.
[windows_keys_to_suppress]
keys = [
    "WIN+LEFT", "WIN+RIGHT", "WIN+UP", "WIN+DOWN",
    "WIN+D", "WIN+TAB",
    "WIN+1", "WIN+2", "WIN+3"
]

# ─────────────────────── Keys to Passthrough ───────────────────────
# Win key combos that should be forwarded to the OS/other apps.
# Since HyprWin intercepts the Win key, these combos are re-injected
# so programs like Raycast, Run dialog, etc. still work.
[windows_keys_to_passthrough]
keys = [
    "WIN+R",       # Windows Run dialog
    "WIN+SPACE"    # Raycast / Input language switch
]

# ─────────────────────── Animations ───────────────────────
[animations]
enabled                = true
window_open_duration_ms  = 200    # Fade/slide-in duration for new windows
window_close_duration_ms = 150    # Fade-out duration when closing
window_move_duration_ms  = 120    # Duration for tiling repositions
easing = "ease_out_cubic"         # Options: linear, ease_in, ease_out, ease_out_cubic, spring

# ─────────────────────── Layout ───────────────────────
[layout]
gaps_inner  = 4    # Pixels between adjacent windows
gaps_outer  = 0    # Pixels from screen edges
border_size = 2    # Active/inactive border thickness
rounding    = 8    # Corner radius in pixels

# ─────────────────────── Theme ───────────────────────
# Colors in hex format. Catppuccin Mocha palette by default.
[theme]
border_active   = "#f77a26"    # Mauve — focused window border
border_inactive = "#45475a"    # Surface1 — unfocused window border
background      = "#1e1e2e"    # Base — general background
top_bar_bg      = "#181825"    # Mantle — top bar background
top_bar_fg      = "#cdd6f4"    # Text — top bar foreground text
top_bar_accent  = "#89b4fa"    # Blue — accent highlights

# ─────────────────────── Top Bar ───────────────────────
[top_bar]
enabled   = true
height    = 30                          # Bar height in pixels
position  = "top"                       # "top" or "bottom"
font      = "JetBrainsMono Nerd Font"   # Font family (fallback: Segoe UI)
font_size = 12                          # Font size in points

# Modules displayed in each section of the bar
[top_bar.modules_left]
modules = ["workspaces"]

[top_bar.modules_center]
modules = ["clock"]

[top_bar.modules_right]
modules = ["tray", "cpu", "cpu_temp", "gpu", "gpu_temp", "memory", "volume"]

# Clock widget configuration
[top_bar.clock]
format      = "HH:mm:ss"               # Time format (.NET format string)
show_date   = true
date_format = "ddd dd.MM.yyyy"          # Date format (.NET format string)

# Workspace indicator configuration
[top_bar.workspaces]
show_count         = 3                  # Number of workspace indicators to show
active_indicator   = "●"               # Symbol for the active workspace
inactive_indicator = "○"               # Symbol for inactive workspaces

# ─────────────────────── Exclusions ───────────────────────
# Programs and window classes to exclude from tiling/management.
# These windows will not be moved, resized, or tiled by HyprWin.
[exclude]
process_names = ["Taskmgr", "3CXDesktopApp", "3CXSoftphone", "mstsc", "msrdc", "3CXWin8Phone", "3CX"]
# Add process names (without .exe) to exclude from tiling, e.g. "vlc", "obs64", "Discord"
# mstsc / msrdc = Remote Desktop Connection windows (excluded to prevent layout interference)
class_names   = []
# Add window class names to exclude, e.g. "TaskManagerWindow", "MozillaDialogClass"
""";
}
