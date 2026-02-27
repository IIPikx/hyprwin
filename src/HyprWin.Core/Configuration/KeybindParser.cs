using HyprWin.Core.Interop;

namespace HyprWin.Core.Configuration;

/// <summary>
/// Parses keybind strings like "SUPER+SHIFT+LEFT" into (Modifiers, VirtualKey) tuples.
/// </summary>
public static class KeybindParser
{
    [Flags]
    public enum Modifiers
    {
        None  = 0,
        Super = 1,
        Shift = 2,
        Ctrl  = 4,
        Alt   = 8
    }

    public record struct Keybind(Modifiers Mods, int VirtualKey)
    {
        public override string ToString()
        {
            var parts = new List<string>();
            if (Mods.HasFlag(Modifiers.Super)) parts.Add("SUPER");
            if (Mods.HasFlag(Modifiers.Ctrl)) parts.Add("CTRL");
            if (Mods.HasFlag(Modifiers.Alt)) parts.Add("ALT");
            if (Mods.HasFlag(Modifiers.Shift)) parts.Add("SHIFT");
            parts.Add(VKeyToString(VirtualKey));
            return string.Join("+", parts);
        }
    }

    private static readonly Dictionary<string, int> _keyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LEFT"]   = NativeMethods.VK_LEFT,
        ["RIGHT"]  = NativeMethods.VK_RIGHT,
        ["UP"]     = NativeMethods.VK_UP,
        ["DOWN"]   = NativeMethods.VK_DOWN,
        ["RETURN"] = NativeMethods.VK_RETURN,
        ["ENTER"]  = NativeMethods.VK_RETURN,
        ["TAB"]    = NativeMethods.VK_TAB,
        ["SPACE"]  = NativeMethods.VK_SPACE,
        ["F11"]    = NativeMethods.VK_F11,
        // Letters A-Z
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44,
        ["E"] = 0x45, ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48,
        ["I"] = 0x49, ["J"] = 0x4A, ["K"] = 0x4B, ["L"] = 0x4C,
        ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F, ["P"] = 0x50,
        ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58,
        ["Y"] = 0x59, ["Z"] = 0x5A,
        // Numbers 0-9
        ["0"] = 0x30, ["1"] = 0x31, ["2"] = 0x32, ["3"] = 0x33,
        ["4"] = 0x34, ["5"] = 0x35, ["6"] = 0x36, ["7"] = 0x37,
        ["8"] = 0x38, ["9"] = 0x39,
        // Function keys
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F12"] = 0x7B,
        // Special
        ["ESCAPE"] = 0x1B, ["ESC"] = 0x1B,
        ["DELETE"] = 0x2E, ["DEL"] = 0x2E,
        ["BACKSPACE"] = 0x08,
        ["INSERT"] = 0x2D,
        ["HOME"] = 0x24,
        ["END"] = 0x23,
        ["PAGEUP"] = 0x21, ["PGUP"] = 0x21,
        ["PAGEDOWN"] = 0x22, ["PGDN"] = 0x22,
    };

    /// <summary>
    /// Parse a keybind string like "SUPER+SHIFT+LEFT" into a Keybind.
    /// </summary>
    public static Keybind Parse(string keybindStr)
    {
        var mods = Modifiers.None;
        int vk = 0;

        var parts = keybindStr.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            switch (upper)
            {
                case "SUPER":
                case "WIN":
                    mods |= Modifiers.Super;
                    break;
                case "SHIFT":
                    mods |= Modifiers.Shift;
                    break;
                case "CTRL":
                case "CONTROL":
                    mods |= Modifiers.Ctrl;
                    break;
                case "ALT":
                    mods |= Modifiers.Alt;
                    break;
                default:
                    if (_keyMap.TryGetValue(upper, out var mapped))
                        vk = mapped;
                    else
                        throw new ArgumentException($"Unknown key: '{part}' in keybind '{keybindStr}'");
                    break;
            }
        }

        if (vk == 0)
            throw new ArgumentException($"No key specified in keybind '{keybindStr}'");

        return new Keybind(mods, vk);
    }

    /// <summary>
    /// Try to parse a keybind, returning null on failure.
    /// </summary>
    public static Keybind? TryParse(string keybindStr)
    {
        try { return Parse(keybindStr); }
        catch { return null; }
    }

    public static string VKeyToString(int vk)
    {
        foreach (var (name, code) in _keyMap)
        {
            if (code == vk) return name;
        }
        return $"0x{vk:X2}";
    }
}
