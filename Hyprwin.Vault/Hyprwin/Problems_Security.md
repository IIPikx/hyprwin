# HyprWin – Bekannte Probleme & Sicherheitshinweise

## Bekannte Limitierungen (Won't Fix / Platform-Bedingt)

### [KNOWN-1] UWP App Refokussierung
UWP-Apps (Microsoft Store-Apps) können nach Focus-Verlust nicht zuverlässig via `SetForegroundWindow` refokussiert werden, da UWP-Fenster über ApplicationFrameHost laufen und `AllowSetForegroundWindow` ignorieren.

### [KNOWN-2] RDP-Session-Kompatibilität
HyprWin verweigert den Start innerhalb von RDP/Terminal-Services-Sessions (abgefangen in `SessionHelper.IsRemoteSession()` → `App.OnStartup`). Globale Keyboard-Hooks (`SetWindowsHookEx WH_KEYBOARD_LL`) funktionieren in RDP-Sessions nicht zuverlässig.

### [KNOWN-3] Windows 11 XAML/Composition Fenster (WS_EX_TOPMOST)
Fenster mit `WS_EX_TOPMOST` und DirectComposition-Rendering (z.B. manche XAML-Hosts) erscheinen schwarz wenn via `SetWindowPos` repositioniert – bekanntes Win32/DWM-Problem auf Windows 11. Kommentar in `NativeMethods.cs`.

### [KNOWN-4] TrayIconService – Cross-Process Memory Reads auf Explorer.exe
`OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ)` + `VirtualAllocEx` + `ReadProcessMemory` greifen direkt in den Explorer-Speicher. Kann von AV/EDR-Software oder Windows Defender Credential Guard blockiert werden; auf Windows 11 mit aktiviertem VBS kann dies stumm fehlschlagen.

**Folge:** Top Bar zeigt keine Tray-Icons. Kein sicherer Alternativansatz ohne Shell-COM-API (`Shell_NotifyIconGetRect`) realisierbar, die die gleichen Daten nicht vollständig liefert.

---

## Behobene Probleme

| Fix | Datei | Beschreibung |
|-----|-------|--------------|
| BUG-1 | `WindowTracker.cs` | `OnWinEventCreate` – tote Lambda entfernt, Tracking-Logik korrekt in `Dispatcher.BeginInvoke` verschoben. `WindowAdded` feuert jetzt auf UI-Thread. |
| BUG-2 | `App.xaml.cs` | `ApplyWindowRuleResult` – `center`-Regel verwendet nun `GetMonitorForWindow()` statt immer Monitor 0. |
| BUG-3 | `SettingsWindow.xaml` | `ThemePresetCombo` und `IconThemeCombo` Steuerelemente im XAML hinzugefügt, sodass Theme-Presets im UI ausgewählt werden können. |
| PERF-1 | `WindowTracker.cs` / `NativeMethods.cs` | `GetProcessNameForWindow` verwendet jetzt `QueryFullProcessImageName` mit `PROCESS_QUERY_LIMITED_INFORMATION` statt verwaltetem `Process`-Objekt. |
| PERF-2 | `SystemInfoService.cs` | Cachen der Netzwerkadapter (`NetworkInterface.GetAllNetworkInterfaces()`) zur Vermeidung wiederholter Kernel IP Helper Enumerationen. |
| PERF-3 | `TrayIconService.cs` | `SetGamingMode` hinzugefügt und Prozess-Lookup optimiert, um CPU- und Memory-Scans bei Spielen zu minimieren. |
| PERF-4 | `MonitorManager.cs` | Zero-Allocation Monitor-Snapshots (`_cachedMonitors`) und `WM_DISPLAYCHANGE` Hook für automatische Display-Neuerkennung. |
| PERF-5 | `AnimationEngine.cs` | Lock-freie Frame-Snapshots während `OnRendering`, `SWP_DEFERERASE` gegen Flackern und `IsPaused` für Gaming-Modus. |
| PERF-6 | `TouchpadGestureService.cs` | Pufferwiederverwendung (`_reportBuffer`) bei Touchpad Raw Input zur Beseitigung von Garbage-Collection-Spikes. |
