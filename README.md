# HyprWin — Hyprland-Inspired Tiling Window Manager for Windows 11

A powerful, customizable tiling window manager for Windows 10/11 that brings the Hyprland experience to the Windows desktop. Built with C# / .NET 8 and WPF.

## Features

### Window Tiling
- **BSP (Binary Space Partitioning) Layout** — intelligent dwindle-style tiling that preserves split ratios
- **Multi-Monitor Support** — each monitor has independent workspaces with seamless cross-monitor window moves
- **Virtual Workspaces** — up to N configurable workspaces per monitor with instant switching
- **DeferWindowPos Batch Positioning** — all window moves are batched into a single DWM recomposition pass for smooth tiling
- **Auto-Float** — popups, dialogs, and fullscreen windows are automatically floated
- **Window Rules** — Hyprland-style `windowrule` for per-app behavior (float, opacity, workspace, size, position)

### Robust Window Closing (SUPER+Q)
- **Proactive BSP Removal** — window is immediately removed from the tiling tree before `WM_CLOSE` is sent, eliminating "ghost node" bugs and layout glitches
- **Smart Escalation** — only force-kills processes that are truly hung (`IsHungAppWindow`), never kills apps showing save dialogs or confirmation prompts
- **Auto Re-Integration** — if a window survives the close (user cancels a dialog), it is automatically re-discovered and re-tiled when it regains focus

### Gaming Mode
- **Auto-Detection** — automatically detects fullscreen games and activates performance mode
- **Suspend Animations** — disables window animations during gaming to eliminate micro-stutters
- **Suspend Border** — hides the border overlay to reduce GPU overhead
- **Reduced Polling** — lowers system info polling frequency from 2s to 10s during gaming
- **Custom Game List** — optionally specify process names that should always trigger gaming mode

### Laptop Support
- **Touchpad Gestures** — 3-finger (or 4-finger) swipe gestures for workspace switching and window management via Windows Precision Touchpad raw HID input
  - Swipe left/right → switch workspaces
  - Swipe down → minimize all windows
  - Configurable finger count and gesture-to-action mapping
  - Automatic detection — works if a Precision Touchpad is present, gracefully disabled otherwise
- **Battery Module** — displays battery percentage, charging status, and dynamic icons in the Top Bar and System Menu
- **Brightness Control** — adjust monitor brightness directly from the System Menu (DDC/CI monitors)

### Top Bar (Taskbar Replacement)
- **Fully Customizable** — configurable height, position (top/bottom), font, font size, colors
- **Modular Design** — enable/disable individual modules: `workspaces`, `clock`, `tray`, `cpu`, `cpu_temp`, `gpu`, `gpu_temp`, `memory`, `volume`, `network`, `battery`
- **Workspace Indicators** — clickable workspace pills with customizable active/inactive symbols
- **System Tray Integration** — displays notification area icons from the hidden Windows taskbar
- **Clock with Calendar** — click the clock to open a calendar popup
- **Battery Module** — displays battery percentage and charging status (laptops)
- **Fullscreen Auto-Hide** — automatically hides when a fullscreen app is detected

### System Menu (macOS Control Center Style)
- **Quick Toggles** — Wi-Fi, Bluetooth, Focus Mode, Nearby Sharing in a grid layout
- **Now Playing** — media controls with track title, artist, and play/pause/skip
- **Brightness Slider** — adjust monitor brightness directly (DDC/CI monitors)
- **Volume Slider** — adjust system volume with mute toggle
- **Network Status** — shows connected network name and type (Wi-Fi/Ethernet)
- **Battery Card** — battery percentage, charging status, and dynamic icon
- **Quick Actions** — Display Settings, Windows Settings, Power options
- **Segoe Fluent Icons** — modern Windows 11 iconography throughout

### Settings Window
- **General** — terminal command, workspace mode, workspace count, autostart
- **Layout** — inner/outer gaps, border size, corner rounding
- **Theme** — all colors configurable (active/inactive border, top bar bg/fg/accent)
- **Top Bar** — enable/disable, height, position, font, font size, module selection
- **Animations** — enable/disable, move duration
- **Gaming Mode** — auto-detect toggle, suspend animations, suspend border
- **Exclusions** — comma-separated process names to exclude from tiling
- **Live Reload** — changes are applied immediately via TOML file watcher

### Animations
- **Slide, Popin, Fade** — Hyprland-compatible window open animation styles
- **Custom Bezier Curves** — define named bezier curves for easing
- **Built-in Easings** — linear, ease_in, ease_out, ease_out_cubic, ease_out_quint, ease_out_expo, spring

### Border Renderer
- **GPU-Accelerated** — uses Win32 region-based rendering (no AllowsTransparency software fallback)
- **Zero-Lag Tracking** — WinEvent-driven position updates for instant border following
- **Customizable** — configurable color, size, and corner rounding

### Additional Features
- **TOML Configuration** — human-readable config with hot-reload at `%APPDATA%\HyprWin\hyprwin.toml`
- **Window Rules** — match by process, class, or title regex with effects (float, opacity, workspace, size, etc.)
- **Custom Launch Shortcuts** — bind any key combo to launch any program
- **Autostart** — optional Windows autostart via registry
- **Tray Icon** — quick access to reload config, open config folder, toggle autostart

## Keyboard Shortcuts

### Window Focus
| Shortcut | Action |
|----------|--------|
| `SUPER + ←/→/↑/↓` | Focus window in direction |

### Window Movement
| Shortcut | Action |
|----------|--------|
| `SUPER + SHIFT + ←/→/↑/↓` | Swap window in direction / move to adjacent monitor |

### Window Resize
| Shortcut | Action |
|----------|--------|
| `SUPER + CTRL + ←/→/↑/↓` | Resize focused window (hold for continuous) |

### Window Actions
| Shortcut | Action |
|----------|--------|
| `SUPER + Q` | Close focused window (proactive BSP removal) |
| `SUPER + T` | Toggle floating |
| `SUPER + F` | Toggle fullscreen |
| `SUPER + M` | Minimize focused window |
| `SUPER + SHIFT + M` | Restore minimized windows on the active workspace |
| `SUPER + D` | Toggle minimize/restore all windows on the active workspace |

### Workspaces
| Shortcut | Action |
|----------|--------|
| `SUPER + 1/2/3` | Switch to workspace 1/2/3 |
| `SUPER + SHIFT + 1/2/3` | Move window to workspace 1/2/3 |

### Layout
| Shortcut | Action |
|----------|--------|
| `SUPER + X` | Set split to horizontal (side-by-side) |
| `SUPER + Y` | Set split to vertical (stacked) |
| `SUPER + SHIFT + X` | Mirror workspace horizontally |
| `SUPER + SHIFT + Y` | Mirror workspace vertically |

### Launch
| Shortcut | Action |
|----------|--------|
| `SUPER + RETURN` | Launch terminal |
| `SUPER + E` | Launch File Explorer |
| `SUPER + I` | Launch Windows Settings |
| `SUPER + B` | Launch default browser |
| `SUPER + SHIFT + S` | Screenshot (Snipping Tool) |
| `SUPER + SHIFT + C` | PowerToys Color Picker |
| `CTRL + SHIFT + ESC` | Task Manager |

### Touchpad Gestures (Laptops)
| Gesture | Default Action |
|---------|---------------|
| 3-finger swipe left | Switch to previous workspace |
| 3-finger swipe right | Switch to next workspace |
| 3-finger swipe down | Minimize all windows |

### System
| Shortcut | Action |
|----------|--------|
| Win key (suppressed) | Start menu via top bar button |
| `WIN + R` | Run dialog (passthrough) |
| `WIN + SPACE` | Input language switch (passthrough) |

## Configuration

Config file: `%APPDATA%\HyprWin\hyprwin.toml`

The config file is auto-generated on first run with inline documentation. Changes are applied instantly — no restart needed.

### Key Sections

```toml
[general]           # Workspace count, terminal, workspace mode, autostart
[keybinds]          # All keyboard shortcuts
[animations]        # Animation styles, durations, easing curves
[layout]            # Gaps, border size, corner rounding
[theme]             # Colors (Catppuccin Mocha default)
[top_bar]           # Bar height, position, font, modules
[gaming]            # Gaming mode: auto-detect, suspend animations/border
[touchpad]          # Touchpad gestures: finger count, swipe actions
[exclude]           # Process/class exclusion lists
[[launch]]          # Custom launch shortcuts
[[window_rule]]     # Per-window behavior rules
[[bezier]]          # Custom easing curves
```

### Touchpad Configuration

```toml
[touchpad]
enabled     = true              # Enable touchpad gesture detection
fingers     = 3                 # Finger count for swipe gestures (3 or 4)
swipe_left  = "workspace_prev"  # Switch to previous workspace
swipe_right = "workspace_next"  # Switch to next workspace
swipe_up    = "none"            # No action
swipe_down  = "minimize_all"    # Minimize all windows on current workspace
```

Available actions: `workspace_prev`, `workspace_next`, `minimize_all`, `none`

## Building

### Prerequisites
- .NET 8 SDK
- Windows 10/11 (Build 22621+)

### Build from Source
```powershell
dotnet build src\HyprWin.App\HyprWin.App.csproj -c Release
```

### Publish Single-File EXE
```powershell
dotnet publish src\HyprWin.App\HyprWin.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

### Build Installer
```powershell
.\publish\build-installer.ps1
```

## Architecture

```
src/
├── HyprWin.App/           # WPF application (UI layer)
│   ├── App.xaml.cs         # Entry point, orchestrates all subsystems
│   ├── TopBarWindow        # Taskbar replacement with modular widgets
│   ├── SystemMenuWindow    # macOS Control Center-style popup
│   ├── SettingsWindow      # Visual configuration editor
│   └── CalendarPopupWindow # Calendar popup for clock widget
│
└── HyprWin.Core/           # Core logic (no UI dependencies)
    ├── TilingEngine         # BSP tree layout with DeferWindowPos batching
    ├── WorkspaceManager     # Virtual workspace management
    ├── WindowTracker        # Win32 event hooks for window lifecycle
    ├── WindowDispatcher     # Keybind action handler (incl. robust close)
    ├── KeyboardHook         # WH_KEYBOARD_LL global hook
    ├── AnimationEngine      # Frame-synced window animations
    ├── BorderRenderer       # GPU-accelerated focus border
    ├── SystemInfoService    # Hardware metrics, media, battery, brightness
    ├── TouchpadGestureService # Raw Input HID touchpad gesture detection
    ├── TaskbarManager       # Native taskbar hide/show
    ├── MonitorManager       # Multi-monitor enumeration
    └── Configuration/       # TOML config parsing with hot-reload
```

## Performance Notes

- **DeferWindowPos** — all tiling operations use `BeginDeferWindowPos`/`EndDeferWindowPos` to batch multiple `SetWindowPos` calls into a single screen update
- **Proactive Close** — `SUPER+Q` removes the window from the BSP tree before sending `WM_CLOSE`, preventing ghost-node tiling bugs
- **Gaming Mode** — automatically reduces overhead when fullscreen games are detected
- **SWP_ASYNCWINDOWPOS** — non-blocking window positioning (Komorebi pattern)
- **Frozen Brushes** — all WPF brushes are frozen for cross-thread safety and GC reduction
- **Dictionary Lookups** — O(1) window handle lookups instead of LINQ queries in hot paths
- **WinEvent Hooks** — zero-polling architecture for window tracking (event-driven, not timer-based)
- **Raw Input Touchpad** — HID-level touchpad parsing with zero cursor interference

## Credits

Inspired by [Hyprland](https://hyprland.org/) and [Komorebi](https://github.com/LGUG2Z/komorebi). Built with love for the Windows tiling community.

## License

MIT License
