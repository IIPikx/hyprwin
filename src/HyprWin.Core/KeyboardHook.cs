using System.Diagnostics;
using System.Runtime.InteropServices;
using HyprWin.Core.Configuration;
using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Low-level keyboard hook engine. Installs WH_KEYBOARD_LL, suppresses configured keys,
/// and dispatches registered keybind actions.
/// Must be installed on a thread with a message pump (WPF UI thread).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc; // prevent GC collection!
    private bool _disposed;

    // Modifier state tracking
    private bool _winDown;
    private bool _shiftDown;
    private bool _ctrlDown;
    private bool _altDown;

    // Win key staleness detection — prevents _winDown getting stuck in VM/RDP
    private long _lastWinEventTick;

    // Registered keybind actions: (Modifiers, VKey) -> Action
    private readonly Dictionary<(KeybindParser.Modifiers, int), Action> _keybindActions = new();

    // Set of (Modifiers, VKey) combos to suppress (from [windows_keys_to_suppress])
    private readonly HashSet<(KeybindParser.Modifiers, int)> _suppressedCombos = new();

    // Set of (Modifiers, VKey) combos to passthrough (re-inject for Win+R, etc.)
    private readonly HashSet<(KeybindParser.Modifiers, int)> _passthroughCombos = new();

    // Key-repeat guard: tracks which (Mods, VKey) combos are currently physically held down.
    // A keybind fires only once per press (on key-down) and re-arms only after key-up.
    // This reliably prevents key-repeat duplicates without any time-based debounce that
    // would accidentally swallow legitimate fast re-presses.
    private readonly HashSet<(KeybindParser.Modifiers, int)> _heldCombos = new();

    public bool IsInstalled => _hookId != IntPtr.Zero;

    public KeyboardHook()
    {
        _hookProc = HookCallback; // prevent delegate GC
    }

    /// <summary>
    /// Install the low-level keyboard hook. MUST be called from a thread with a message pump.
    /// </summary>
    public void Install()
    {
        if (_hookId != IntPtr.Zero)
        {
            Logger.Instance.Warn("Keyboard hook already installed");
            return;
        }

        using var proc = Process.GetCurrentProcess();
        using var mod = proc.MainModule!;
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(mod.ModuleName),
            0);

        if (_hookId == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Logger.Instance.Error($"Failed to install keyboard hook. Win32 error: {error}");
            throw new InvalidOperationException($"SetWindowsHookEx failed with error {error}");
        }

        // Clear any stuck Win key state from a previous session
        _winDown = false;
        NativeMethods.keybd_event((byte)NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event((byte)NativeMethods.VK_RWIN, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

        Logger.Instance.Info("Low-level keyboard hook installed");
    }

    /// <summary>
    /// Register a keybind that triggers an action when pressed.
    /// </summary>
    public void RegisterKeybind(string keybindStr, Action action)
    {
        var kb = KeybindParser.TryParse(keybindStr);
        if (kb is null)
        {
            Logger.Instance.Warn($"Failed to parse keybind: '{keybindStr}'");
            return;
        }

        var key = (kb.Value.Mods, kb.Value.VirtualKey);
        if (_keybindActions.ContainsKey(key))
        {
            Logger.Instance.Warn($"Keybind conflict: '{keybindStr}' is already registered. Skipping.");
            return;
        }

        _keybindActions[key] = action;
        Logger.Instance.Debug($"Registered keybind: {keybindStr} → {kb.Value}");
    }

    /// <summary>
    /// Register a key combo to be suppressed (native Windows shortcut).
    /// </summary>
    public void RegisterSuppression(string comboStr)
    {
        var normalized = comboStr.Replace("WIN", "SUPER", StringComparison.OrdinalIgnoreCase);
        var kb = KeybindParser.TryParse(normalized);
        if (kb is null)
        {
            Logger.Instance.Warn($"Failed to parse suppression key: '{comboStr}'");
            return;
        }

        _suppressedCombos.Add((kb.Value.Mods, kb.Value.VirtualKey));
        Logger.Instance.Debug($"Registered suppression: {comboStr}");
    }

    /// <summary>
    /// Register a key combo to be passed through to the system (e.g., Win+R for Run).
    /// Since we suppress the Win key, passthrough combos are re-injected via keybd_event.
    /// </summary>
    public void RegisterPassthrough(string comboStr)
    {
        var normalized = comboStr.Replace("WIN", "SUPER", StringComparison.OrdinalIgnoreCase);
        var kb = KeybindParser.TryParse(normalized);
        if (kb is null)
        {
            Logger.Instance.Warn($"Failed to parse passthrough key: '{comboStr}'");
            return;
        }

        _passthroughCombos.Add((kb.Value.Mods, kb.Value.VirtualKey));
        Logger.Instance.Debug($"Registered passthrough: {comboStr}");
    }

    /// <summary>
    /// Clear all registered keybinds, suppressions, and passthroughs (for config reload).
    /// </summary>
    public void ClearRegistrations()
    {
        _keybindActions.Clear();
        _suppressedCombos.Clear();
        _passthroughCombos.Clear();
        _heldCombos.Clear();
        Logger.Instance.Debug("Cleared all keybind registrations");
    }

    /// <summary>
    /// Register all keybinds, suppressions, and passthroughs from config.
    /// </summary>
    public void RegisterFromConfig(HyprWinConfig config)
    {
        // Suppressions
        foreach (var key in config.WindowsKeysToSuppress.Keys)
            RegisterSuppression(key);

        // Passthroughs
        foreach (var key in config.WindowsKeysToPassthrough.Keys)
            RegisterPassthrough(key);

        Logger.Instance.Debug("Win key Start menu suppression enabled");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var kbd = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();
                int vk = (int)kbd.vkCode;

                // Skip injected events (our own keybd_event calls) to avoid recursion
                if ((kbd.flags & NativeMethods.LLKHF_INJECTED) != 0)
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

                bool isKeyDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                bool isKeyUp = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

                // ── Track modifier state ──
                if (vk is NativeMethods.VK_LWIN or NativeMethods.VK_RWIN)
                {
                    _lastWinEventTick = Environment.TickCount64;
                    if (isKeyDown)
                    {
                        _winDown = true;
                        return (IntPtr)1; // Suppress Win key to prevent Start menu
                    }
                    else if (isKeyUp)
                    {
                        _winDown = false;
                        _heldCombos.Clear(); // All Super+X combos are now invalid
                        return (IntPtr)1; // Suppress Win key up
                    }
                }

                if (vk is NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT or NativeMethods.VK_SHIFT)
                {
                    _shiftDown = isKeyDown;
                }
                else if (vk is NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL or NativeMethods.VK_CONTROL)
                {
                    _ctrlDown = isKeyDown;
                }
                else if (vk is NativeMethods.VK_LMENU or NativeMethods.VK_RMENU or NativeMethods.VK_MENU)
                {
                    _altDown = isKeyDown;
                }

                // ── Process key-down events for actions ──
                if (isKeyDown && !IsModifierKey(vk))
                {
                    // Guard: reset stale Win key state (handles lost key-up in VM/RDP)
                    if (_winDown && (Environment.TickCount64 - _lastWinEventTick) > 2000)
                    {
                        Logger.Instance.Debug("Stale Win key state detected (>2s since last Win event), resetting");
                        _winDown = false;
                        _heldCombos.Clear();
                    }

                    var currentMods = GetCurrentModifiers();
                    var comboKey = (currentMods, vk);

                    // 1. Check passthrough combos FIRST (Win+R, Win+Space, etc.)
                    if (_passthroughCombos.Contains(comboKey))
                    {
                        // Re-inject via keybd_event so the system sees the full combo
                        InjectWinCombo(vk, currentMods);
                        return (IntPtr)1; // Suppress the original (injected copy will pass through)
                    }

                    // 2. Check registered keybinds
                    if (_keybindActions.TryGetValue(comboKey, out var action))
                    {
                        // Only fire once per physical key-down; re-arms on key-up.
                        // This prevents key-repeat from firing the action repeatedly.
                        if (!_heldCombos.Contains(comboKey))
                        {
                            _heldCombos.Add(comboKey);
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);
                        }
                        return (IntPtr)1; // Always suppress (even on repeat)
                    }

                    // 3. Check suppressed combos
                    if (_suppressedCombos.Contains(comboKey))
                    {
                        return (IntPtr)1; // Suppress
                    }

                    // 4. When SUPER is held, swallow every key that didn't match any of the above.
                    //    This prevents unintended input leaking to the focused application
                    //    (e.g. arrow keys moving the cursor in an editor while SUPER is held).
                    if (_winDown)
                    {
                        return (IntPtr)1;
                    }
                }

                // ── On key-up: disarm held-combo guard so the next press can fire again ──
                if (isKeyUp && !IsModifierKey(vk))
                {
                    var currentMods = GetCurrentModifiers();
                    _heldCombos.Remove((currentMods, vk));
                    // Also remove with Super modifier in case SUPER was released before the key
                    var modsWithSuper = currentMods | KeybindParser.Modifiers.Super;
                    var modsWithoutSuper = currentMods & ~KeybindParser.Modifiers.Super;
                    _heldCombos.Remove((modsWithSuper, vk));
                    _heldCombos.Remove((modsWithoutSuper, vk));
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("Exception in keyboard hook callback", ex);
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Re-inject a Win+key combo via keybd_event so the system handles it normally.
    /// The injected events have LLKHF_INJECTED flag set and will pass through our hook.
    /// </summary>
    private static void InjectWinCombo(int vk, KeybindParser.Modifiers mods)
    {
        // Press Win
        NativeMethods.keybd_event((byte)NativeMethods.VK_LWIN, 0, 0, UIntPtr.Zero);
        try
        {
            // Press modifiers if needed
            if (mods.HasFlag(KeybindParser.Modifiers.Shift))
                NativeMethods.keybd_event((byte)NativeMethods.VK_LSHIFT, 0, 0, UIntPtr.Zero);
            if (mods.HasFlag(KeybindParser.Modifiers.Ctrl))
                NativeMethods.keybd_event((byte)NativeMethods.VK_LCONTROL, 0, 0, UIntPtr.Zero);
            if (mods.HasFlag(KeybindParser.Modifiers.Alt))
                NativeMethods.keybd_event((byte)NativeMethods.VK_LMENU, 0, 0, UIntPtr.Zero);

            // Press and release the key
            NativeMethods.keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event((byte)vk, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

            // Release modifiers
            if (mods.HasFlag(KeybindParser.Modifiers.Alt))
                NativeMethods.keybd_event((byte)NativeMethods.VK_LMENU, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (mods.HasFlag(KeybindParser.Modifiers.Ctrl))
                NativeMethods.keybd_event((byte)NativeMethods.VK_LCONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (mods.HasFlag(KeybindParser.Modifiers.Shift))
                NativeMethods.keybd_event((byte)NativeMethods.VK_LSHIFT, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        finally
        {
            // ALWAYS release Win — even if an exception occurs mid-injection,
            // we must not leave the OS with Win key stuck as pressed.
            NativeMethods.keybd_event((byte)NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }

    private KeybindParser.Modifiers GetCurrentModifiers()
    {
        var mods = KeybindParser.Modifiers.None;
        if (_winDown) mods |= KeybindParser.Modifiers.Super;
        if (_shiftDown) mods |= KeybindParser.Modifiers.Shift;
        if (_ctrlDown) mods |= KeybindParser.Modifiers.Ctrl;
        if (_altDown) mods |= KeybindParser.Modifiers.Alt;
        return mods;
    }

    private static bool IsModifierKey(int vk)
    {
        return vk is NativeMethods.VK_LWIN or NativeMethods.VK_RWIN
            or NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT or NativeMethods.VK_SHIFT
            or NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL or NativeMethods.VK_CONTROL
            or NativeMethods.VK_LMENU or NativeMethods.VK_RMENU or NativeMethods.VK_MENU;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            Logger.Instance.Info("Keyboard hook uninstalled");
        }
    }
}
