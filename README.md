# HyprWin

Hyprland-inspired tiling window manager for Windows 11.

> WPF · .NET 8 · Self-contained single-file EXE · No admin required*

---

## Features

### Window Management
- **BSP tiling engine** — Binary Space Partitioning layout; every new window automatically splits the available space
- **Virtual workspaces** — up to N independent workspaces per monitor (default: 3)
- **Two workspace modes**: `monitor_bound` (workspace N maps to monitor N) or `virtual` (per-monitor virtual desktops)
- **Float / fullscreen toggle** — switch any tiled window to floating or fullscreen on the fly
- **Window swap** — mirror all windows left↔right or top↔bottom on the active workspace
- **Split rotation** — change the split direction of the focused window (horizontal ↔ vertical)
- **Mouse-free focus navigation** — move focus between windows purely with the keyboard
- **Window rules** — Hyprland-style `[[window_rule]]` entries: auto-float, pin, assign to workspace, set opacity, custom border, size, position, and more
- **Exclusion list** — exclude specific processes or window classes from tiling entirely

### Top Bar
- Replaces the native Windows taskbar (hidden non-destructively; tray icons remain functional)
- Fully configurable left / center / right module layout
- Available modules: `workspaces`, `clock`, `cpu`, `cpu_temp`, `gpu`, `gpu_temp`, `memory`, `network`, `volume`, `tray`
- Hardware sensor data via LibreHardwareMonitor (CPU/GPU temperature, GPU load)
- System audio volume via WASAPI (no external dependency)
- Network status: connection type (Wi-Fi/Ethernet), up/down speed
- Media "Now Playing" info (Windows Runtime)
- Bluetooth availability and state (Windows Runtime)
- Configurable font, font size, bar height, and position (top or bottom)
- Catppuccin Mocha color palette by default — fully customizable via hex colors

### Keyboard & Shortcuts
- Global low-level keyboard hook (`SetWindowsHookEx WH_KEYBOARD_LL`)
- All shortcuts fully remappable via `[keybinds]` in `hyprwin.toml`
- Selective suppression of native Windows shortcuts (`WIN+LEFT`, `WIN+D`, `WIN+TAB`, etc.)
- Passthrough list for shortcuts that should still reach the OS (`WIN+R`, `WIN+SPACE`)
- Custom launch shortcuts for any executable via `[[launch]]` entries

### Configuration
- TOML config at `%APPDATA%\HyprWin\hyprwin.toml`
- **Hot-reload** — changes take effect on save, no restart needed
- Full inline documentation generated on first run

### Animations
- Window open / close / move animations with configurable duration
- Styles: `slide`, `popin`, `fade`
- Easing: `linear`, `ease_in`, `ease_out`, `ease_out_cubic`, `ease_out_quint`, `ease_out_expo`, `ease_in_out_cubic`, `spring`, or any named custom bezier curve
- Custom bezier curves definable via `[[bezier]]`

### Other
- **Autostart** with Windows via registry (`HKCU\...\Run`)
- Tray icon with quick access to Settings, System Menu, and Quit
- Colored window border overlay for the focused window (GPU-accelerated, click-through)
- Detailed log at `%APPDATA%\HyprWin\hyprwin.log`

---

## Default Keyboard Shortcuts

All shortcuts are remappable in `hyprwin.toml`. Modifiers: `SUPER`, `SHIFT`, `CTRL`, `ALT`.

### Focus & Navigation
| Shortcut | Action |
|---|---|
| `SUPER + ←/→/↑/↓` | Focus window in direction |

### Move & Resize
| Shortcut | Action |
|---|---|
| `SUPER + SHIFT + ←/→/↑/↓` | Move focused window in direction |
| `SUPER + CTRL + ←/→/↑/↓` | Resize focused window |

### Window State
| Shortcut | Action |
|---|---|
| `SUPER + Q` | Close focused window |
| `SUPER + T` | Toggle float |
| `SUPER + F` | Toggle fullscreen |
| `SUPER + D` | Minimize all windows on the active workspace |

### Layout
| Shortcut | Action |
|---|---|
| `SUPER + X` | Set active window's split to top/bottom |
| `SUPER + Y` | Set active window's split to side-by-side |
| `SUPER + SHIFT + X` | Mirror all windows left↔right |
| `SUPER + SHIFT + Y` | Mirror all windows top↔bottom |

### Workspaces
| Shortcut | Action |
|---|---|
| `SUPER + 1 / 2 / 3` | Switch to workspace 1 / 2 / 3 |
| `SUPER + SHIFT + 1 / 2 / 3` | Move focused window to workspace 1 / 2 / 3 |

### Launch
| Shortcut | Action |
|---|---|
| `SUPER + RETURN` | Launch terminal (`wt.exe`) |
| `SUPER + E` | Launch File Explorer |
| `SUPER + B` | Open default browser |
| `SUPER + I` | Open Windows Settings |
| `SUPER + SHIFT + S` | Screenshot (Windows Snipping Tool) |
| `SUPER + SHIFT + C` | PowerToys Color Picker |
| `CTRL + SHIFT + ESC` | Task Manager |

> Custom shortcuts for any executable can be added via `[[launch]]` entries in `hyprwin.toml`.

---

## Requirements

- Windows 11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) *(only needed to build from source)*

---

## Building from Source

### Single-file EXE

```powershell
dotnet publish src\HyprWin.App\HyprWin.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish
```

The output EXE is placed in `publish\`. It is **self-contained** — no .NET runtime required on the target machine.

### Build only (no publish)

```powershell
dotnet build HyprWin.sln -c Release
```

---

## Configuration

On first launch, HyprWin writes a fully documented config file to:

```
%APPDATA%\HyprWin\hyprwin.toml
```

The file is **hot-reloaded** on every save — no restart needed.

### Config sections

| Section | Description |
|---|---|
| `[general]` | Workspace count, terminal command, workspace mode, autostart |
| `[keybinds]` | All keyboard shortcuts |
| `[layout]` | Inner/outer gaps, border thickness, corner rounding |
| `[theme]` | Border and top bar colors (hex) |
| `[animations]` | Enable/disable, duration, easing, open style |
| `[top_bar]` | Modules, font, height, position, clock/workspace config |
| `[exclude]` | Processes / window classes excluded from tiling |
| `[[launch]]` | Custom program launch shortcuts |
| `[[window_rule]]` | Hyprland-style window rules |
| `[[bezier]]` | Named custom bezier curves for animations |
| `[windows_keys_to_suppress]` | Native Win key combos to intercept and block |
| `[windows_keys_to_passthrough]` | Win key combos forwarded to the OS |

### Window rule effects

`float`, `fullscreen`, `workspace`, `pin`, `center`, `no_anim`, `opacity`, `border_color`, `border_size`, `size`, `move`

---

## Known Issues & Limitations

- **`WIN+L` and `Ctrl+Alt+Del` cannot be suppressed** — these are handled at the Windows secure desktop level and are outside the reach of any user-mode hook.
- **CPU/GPU temperature requires administrator privileges** — LibreHardwareMonitor needs ring-0 driver access for sensor readings. Without admin rights, temperature values show as `0`; all other features work normally.
- **Some UWP / Store apps resist repositioning** — certain modern Windows apps ignore `SetWindowPos` calls. They appear correctly in the BSP tree but may not visually snap to their assigned position.
- **Task Manager shortcut** (`CTRL+SHIFT+ESC`) is blocked by the low-level hook and must be explicitly listed in `[keybinds]` to work. This is the default configuration; removing the entry disables the shortcut entirely.
- **Remote Desktop (RDP) sessions** — multi-monitor behavior across RDP boundaries may be unreliable. HyprWin detects RDP sessions but does not have special handling for them.
- **Tray icons** — icons are read from the hidden native taskbar's `ToolbarWindow32` child. Newly added tray icons may occasionally require a few seconds to appear in the custom bar.
- **Media info lag** — the "Now Playing" widget is populated via Windows Runtime asynchronously and may be up to one poll cycle (~2 s) behind the current playback state.
- **Animations in virtual machines** — some VM environments lack proper GPU or compositor support, which can cause animation glitches. HyprWin catches and suppresses these errors automatically but animations may look degraded.

---

## Project Structure

```
HyprWin.sln
src/
  HyprWin.App/          # WPF frontend: TopBarWindow, SettingsWindow, SystemMenuWindow, Tray
  HyprWin.Core/         # Core logic
    Configuration/      # Config model & TOML parser (live reload)
    Interop/            # Win32 P/Invoke declarations
    AnimationEngine.cs
    AudioManager.cs
    BorderRenderer.cs
    HardwareMonitor.cs
    KeyboardHook.cs
    SystemInfoService.cs
    TaskbarManager.cs
    TilingEngine.cs     # BSP layout engine
    WindowDispatcher.cs
    WindowRuleEngine.cs
    WindowTracker.cs
    WorkspaceManager.cs
    ...
```

---
