# HyprWin

Hyprland-inspired tiling window manager for Windows 11.

## Voraussetzungen

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 11 (x64)

## Executable bauen & veröffentlichen

### Schnellstart (Single-File EXE)

```powershell
dotnet publish src\HyprWin.App\HyprWin.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish
```

Die fertige `HyprWin.App.exe` landet im Ordner `publish\`.  
Sie ist **self-contained** – auf dem Zielsystem muss keine .NET-Laufzeit installiert sein.

### Nur bauen (ohne Publish)

```powershell
dotnet build HyprWin.sln -c Release
```

### NuGet-Pakete wiederherstellen

```powershell
dotnet restore HyprWin.sln
```

## Konfiguration

Beim ersten Start wird automatisch eine Konfigurationsdatei angelegt:

```
%APPDATA%\HyprWin\hyprwin.toml
```

Eine kommentierte Standardkonfiguration liegt dem Release unter `publish\hyprwin.toml` bei.  
Die Datei wird **live neu geladen**, sobald sie gespeichert wird – kein Neustart nötig.

### Wichtige Abschnitte

| Abschnitt | Beschreibung |
|---|---|
| `[general]` | Anzahl Workspaces, Terminal-Befehl, Workspace-Modus |
| `[keybinds]` | Alle Tastenkürzel (Modifier: `SUPER`, `SHIFT`, `CTRL`, `ALT`) |
| `[layout]` | Lücken zwischen Fenstern, Rahmenbreite, Ecken-Radius |
| `[theme]` | Rahmenfarben und Topbar-Farben (Hex) |
| `[animations]` | Animationen ein/aus, Dauer, Easing-Funktion |
| `[top_bar]` | Topbar-Module, Uhrformat, Workspace-Indikatoren |
| `[exclude]` | Prozesse / Fensterklassen vom Tiling ausschließen |

## Projektstruktur

```
HyprWin.sln
publish/                        ← Ausgabe nach dotnet publish
src/
  HyprWin.App/                  ← WPF-Frontend (TopBar, Settings, Tray)
  HyprWin.Core/                 ← Kern-Logik
    Configuration/              ← Config-Modell & TOML-Parser
    Interop/                    ← Win32 P/Invoke
    AnimationEngine.cs
    BorderRenderer.cs
    KeyboardHook.cs
    TilingEngine.cs
    WindowTracker.cs
    WorkspaceManager.cs
    ...
```
