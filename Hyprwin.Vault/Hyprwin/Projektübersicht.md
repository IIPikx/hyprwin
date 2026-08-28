# HyprWin Projektübersicht
## HyprWin.App\App.xaml.cs
- **class App**
  - **Methoden:**
    - OnStartup
    - OnExit
    - InstallMessageWindow
    - MessageWindowProc
    - OnMonitorChanged
    - SetGamingMode
    - RegisterKeybinds
    - OnWindowAdded
    - OnWindowRemoved
    - OnWindowMinimized
    - OnWindowRestored
    - OnFocusChanged
    - OnConfigChanged
    - CreateTopBars
    - CreateTrayIcon
    - RegisterBeziers
    - LoadWindowRules
    - ApplyWindowRuleResult

## HyprWin.App\CalendarPopupWindow.xaml.cs
- **class CalendarPopupWindow**
  - **Methoden:**
    - BuildCalendar
    - DayOfWeekOffset
    - EmptyCell
    - Frozen
    - PrevMonth_Click
    - NextMonth_Click
    - MonthYearLabel_Click
    - Window_MouseWheel
    - Window_Deactivated
    - OnClosed
    - OnSourceInitialized

## HyprWin.App\SettingsWindow.xaml.cs
- **class SettingsWindow**
  - **Methoden:**
    - LoadValues
    - Save_Click
    - OpenToml_Click
    - SetTomlValue
    - SetTomlValueInSection
    - SetTomlStringValue
    - SetTomlArrayValue

## HyprWin.App\SystemMenuWindow.xaml.cs
- **class SystemMenuWindow**
  - **Methoden:**
    - OnSourceInitialized
    - MakeFrozen
    - OnMetricsUpdated
    - ApplyMetrics
    - BtnPrev_Click
    - BtnPlayPause_Click
    - BtnNext_Click
    - BtnMute_Click
    - VolumeSlider_ValueChanged
    - BtnBluetooth_Click
    - BtnClose_Click
    - OnClosed

## HyprWin.App\TopBarWindow.xaml.cs
- **class TopBarWindow**
- **struct WINDOWPOS**
- **class WorkspaceItem**
  - **Methoden:**
    - OnSourceInitialized
    - WndProc
    - OnDeactivated
    - OnStateChanged
    - ApplyConfig
    - PositionOnMonitor
    - SetupTimers
    - SetGamingMode
    - CheckFullscreen
    - RestoreBarIfNeeded
    - UpdateClock
    - OnMetricsUpdated
    - ApplySystemMetrics
    - FormatBytesRate
    - UpdateWorkspaceIndicators
    - SetConfigPath
    - WorkspaceButton_Click
    - OnTrayIconsUpdated
    - TrayIcon_LeftClick
    - TrayIcon_RightClick
    - BarBorder_RightClick
    - WindowsMenuButton_Click
    - TaskManagerButton_Click
    - SystemMenuButton_Click
    - SettingsButton_Click
    - ClockText_Click
    - BrushFromHex
    - OnClosed

## HyprWin.Core\AnimationEngine.cs
- **class ActiveAnimation**
- **enum AnimationStyle**
- **class CubicBezier**
- **class Easing**
- **class AnimationEngine**
  - **Methoden:**
    - IsComplete
    - GetProgress
    - BuildLut
    - SampleX
    - SampleY
    - Evaluate
    - Linear
    - EaseIn
    - EaseOut
    - EaseOutCubic
    - EaseOutQuint
    - EaseOutExpo
    - EaseInOutCubic
    - Spring
    - RegisterBezier
    - ClearBeziers
    - ParseStyle
    - UpdateFromConfig
    - AnimateMove
    - AnimateOpen
    - IsAnimating
    - StartRendering
    - StopRendering
    - OnRendering
    - Lerp
    - Dispose

## HyprWin.Core\AudioManager.cs
- **class AudioManager**
- **interface IDs**
- **interface IMMDeviceEnumerator**
- **interface IMMDevice**
- **interface IAudioEndpointVolume**
- **class MMDeviceEnumeratorCoClass**
  - **Methoden:**
    - GetVolume
    - SetVolume
    - IsMuted
    - ToggleMute
    - GetVolumeInterface

## HyprWin.Core\AutostartManager.cs
- **class AutostartManager**
  - **Methoden:**
    - IsEnabled
    - Enable
    - Disable
    - SetEnabled
    - GetExePath

## HyprWin.Core\BorderRenderer.cs
- **class BorderRenderer**
  - **Methoden:**
    - Start
    - UpdateTheme
    - TrackWindow
    - OnLocationChanged
    - OnFallbackTick
    - UpdateBorderPositionDirect
    - UpdateWindowRegion
    - BrushFromHex
    - Dispose

## HyprWin.Core\HardwareMonitor.cs
- **class HardwareMonitor**
  - **Methoden:**
    - Initialize
    - Update
    - AllSensors
    - Dispose

## HyprWin.Core\IpcServer.cs
- **class IpcServer**
  - **Methoden:**
    - Start
    - ServerLoopAsync
    - Dispose

## HyprWin.Core\KeyboardHook.cs
- **class KeyboardHook**
  - **Methoden:**
    - Install
    - RegisterKeybind
    - RegisterSuppression
    - RegisterPassthrough
    - RegisterRepeatableKeybind
    - ClearRegistrations
    - RegisterFromConfig
    - HookCallback
    - InjectWinCombo
    - IsModifierKey
    - Dispose

## HyprWin.Core\MouseHook.cs
- **class MouseHook**
  - **Methoden:**
    - Install
    - HookCallback
    - IsSuperDown
    - OnLeftButtonDown
    - OnMouseMove
    - OnLeftButtonUp
    - OnRightButtonDown
    - OnRightButtonUp
    - Dispose

## HyprWin.Core\Logger.cs
- **class Logger**
- **enum Level**
  - **Methoden:**
    - GetDefaultLogPath
    - Initialize
    - Log
    - Info
    - Warn
    - Error
    - Error
    - Debug
    - Dispose

## HyprWin.Core\MonitorManager.cs
- **record MonitorInfo**
- **class MonitorManager**
  - **Methoden:**
    - Enumerate
    - OnDisplayChange
    - GetByHandle
    - GetByIndex
    - GetMonitorForWindow
    - GetMonitorAtCursor

## HyprWin.Core\SessionHelper.cs
- **class to**
- **class SessionHelper**
  - **Methoden:**
    - IsRemoteSession

## HyprWin.Core\SystemInfoService.cs
- **class SystemMetrics**
- **interface enumeration**
- **interface list**
- **class SystemInfoService**
- **interface list**
- **interface list**
- **interface list**
  - **Methoden:**
    - Start
    - Poll
    - ReadCpu
    - RefreshMediaAsync
    - RefreshBluetoothAsync
    - ToggleBluetoothAsync
    - MediaPlayPauseAsync
    - MediaPreviousAsync
    - MediaNextAsync
    - Dispose

## HyprWin.Core\TaskbarManager.cs
- **class TaskbarManager**
  - **Methoden:**
    - HideTaskbar
    - ShowTaskbar
    - ReHideIfNeeded
    - Dispose

## HyprWin.Core\TilingEngine.cs
- **class BspNode**
- **enum SplitDirection**
- **class TilingEngine**
  - **Methoden:**
    - Leaf
    - Split
    - FindLeaf
    - GetLeaves
    - LeafCount
    - UpdateLayout
    - AddWindow
    - GetNodeDepth
    - RemoveWindow
    - TileWorkspace
    - CalculateLayout
    - ApplyWindowPosition
    - SwapWindows
    - RotateSplitToDirection
    - MirrorHorizontal
    - MirrorVertical
    - MirrorNode
    - ResizeInDirection
    - SyncTree
    - RebuildTree
    - BuildBalancedSubtree

## HyprWin.Core\TrayIconService.cs
- **class TrayIconInfo**
- **class TrayIconService**
  - **Methoden:**
    - Start
    - SetGamingMode
    - PollIcons
    - ReadTrayIcons
    - EnumToolbarChildren
    - ReadToolbarIcons
    - IconToImageSource
    - SendIconClick
    - SendIconDoubleClick
    - Dispose

## HyprWin.Core\WindowDispatcher.cs
- **class WindowDispatcher**
  - **Methoden:**
    - SetTerminalCommand
    - SetWorkspaceMode
    - FocusLeft
    - FocusRight
    - FocusUp
    - FocusDown
    - FocusInDirection
    - MoveLeft
    - MoveRight
    - MoveUp
    - MoveDown
    - SwapHorizontal
    - RotateSplitVertical
    - RotateSplitHorizontal
    - SwapVertical
    - ResizeLeft
    - ResizeRight
    - ResizeUp
    - ResizeDown
    - ResizeInDirection
    - SwapInDirection
    - CloseWindow
    - ToggleFloat
    - ToggleFullscreen
    - SwitchToWorkspace
    - MoveToWorkspace
    - FocusMonitor
    - MoveToMonitor
    - MinimizeAll
    - LaunchExplorer
    - TakeScreenshot
    - LaunchTerminal
    - LaunchTaskManager
    - LaunchProgram
    - GetAdjacentMonitor

## HyprWin.Core\WindowRuleEngine.cs
- **class WindowRule**
- **class WindowRuleEngine**
- **class WindowRuleResult**
  - **Methoden:**
    - Matches
    - SetRules
    - Evaluate

## HyprWin.Core\WindowTracker.cs
- **class ManagedWindow**
- **class WindowTracker**
- **class names**
- **class blocklist**
- **class names**
  - **Methoden:**
    - RefreshBounds
    - RefreshTitle
    - ToString
    - GetWindow
    - SetExclusions
    - RestoreAllWindows
    - GetProcessNameForWindow
    - Start
    - EnumerateExistingWindows
    - TrackWindow
    - UntrackWindow
    - IsManageableWindow
    - IsPopupOrDialog
    - IsPictureInPictureWindow
    - IsSystemWindow
    - OnWinEventCreate
    - OnWinEventDestroy
    - OnWinEventForeground
    - OnWinEventMoveSizeEnd
    - OnWinEventMinimizeStart
    - OnWinEventMinimizeEnd
    - Dispose

## HyprWin.Core\WorkspaceManager.cs
- **class Workspace**
- **class WorkspaceManager**
  - **Methoden:**
    - Initialize
    - AssignExistingWindows
    - GetWorkspace
    - GetActiveWorkspace
    - GetActiveWorkspaceIndex
    - SwitchWorkspace
    - AddWindowToActiveWorkspace
    - RemoveWindow
    - FindWorkspaceForWindow
    - MoveWindowToWorkspace
    - MoveWindowToMonitor
    - UpdateFocus
    - GetFocusedMonitorIndex

## HyprWin.Core\Configuration\ConfigManager.cs
- **class ConfigManager**
  - **Methoden:**
    - ResolveConfigPath
    - GetDefaultConfigPath
    - Load
    - StartWatching
    - OnFileChanged
    - OnDebounceElapsed
    - ParseConfig
    - ParseGeneral
    - ParseKeybinds
    - ParseSuppressedKeys
    - ParsePassthroughKeys
    - ParseAnimations
    - ParseLayout
    - ParseTheme
    - ParseTopBar
    - ParseModules
    - ParseClock
    - ParseWorkspacesWidget
    - ParseLaunchEntries
    - ParseWindowRules
    - ParseBeziers
    - ParseExclude
    - GetString
    - GetStringOrNull
    - GetInt
    - GetIntOrNull
    - GetDouble
    - GetDoubleOrNull
    - GetBool
    - GetBoolOrNull
    - Dispose

## HyprWin.Core\Configuration\DefaultConfig.cs
- **class DefaultConfig**
- **class names**

## HyprWin.Core\Configuration\HyprWinConfig.cs
- **class HyprWinConfig**
- **class GeneralConfig**
- **class KeybindsConfig**
- **class WindowsKeysToSuppressConfig**
- **class WindowsKeysToPassthroughConfig**
- **class AnimationsConfig**
- **class LayoutConfig**
- **class ThemeConfig**
- **class TopBarConfig**
- **class TopBarModulesConfig**
- **class ClockConfig**
- **class WorkspacesWidgetConfig**
- **class WindowRuleConfig**
- **class regex**
- **class BezierConfig**
- **class LaunchEntry**
- **class ExcludeConfig**
- **class names**

## HyprWin.Core\Configuration\KeybindParser.cs
- **class KeybindParser**
- **enum Modifiers**
- **record struct**
  - **Methoden:**
    - ToString
    - Parse
    - TryParse
    - VKeyToString

## HyprWin.Core\Interop\NativeMethods.cs
- **class NativeMethods**
- **struct KBDLLHOOKSTRUCT**
- **struct RECT**
- **struct POINT**
- **struct MONITORINFOEX**
- **struct APPBARDATA**
- **struct MEMORYSTATUSEX**
- **struct WINDOWPLACEMENT**
- **struct INPUT**
- **struct INPUTUNION**
- **struct MOUSEINPUT**
  - **Methoden:**
    - ToString
    - GetOwner
    - GetWindowTitle
    - GetWindowClassName
    - IsWindowCloaked
    - GetExtendedFrameBounds
    - SetCornerPreference
    - ForceForegroundWindow
    - DisableForegroundLockTimeout

