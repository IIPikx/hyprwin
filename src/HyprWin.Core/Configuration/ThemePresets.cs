namespace HyprWin.Core.Configuration;

/// <summary>
/// Predefined color theme presets for quick theme switching.
/// Each preset defines all theme colors (borders, top bar, background).
/// </summary>
public static class ThemePresets
{
    public record ColorTheme(
        string Name,
        string BorderActive,
        string BorderInactive,
        string Background,
        string TopBarBg,
        string TopBarFg,
        string TopBarAccent
    );

    /// <summary>
    /// Icon theme presets for top bar system metrics display.
    /// </summary>
    public record IconTheme(
        string Name,
        string Cpu,
        string Gpu,
        string Ram,
        string VolumeOn,
        string VolumeMuted,
        string NetDown,
        string NetUp,
        string Temp,
        string ActiveWorkspace,
        string InactiveWorkspace
    );

    public static readonly ColorTheme[] ColorThemes =
    [
        new("Catppuccin Mocha",
            BorderActive: "#f77a26",
            BorderInactive: "#45475a",
            Background: "#1e1e2e",
            TopBarBg: "#181825",
            TopBarFg: "#cdd6f4",
            TopBarAccent: "#89b4fa"),

        new("Catppuccin Macchiato",
            BorderActive: "#c6a0f6",
            BorderInactive: "#494d64",
            Background: "#24273a",
            TopBarBg: "#1e2030",
            TopBarFg: "#cad3f5",
            TopBarAccent: "#8aadf4"),

        new("Catppuccin Latte",
            BorderActive: "#8839ef",
            BorderInactive: "#bcc0cc",
            Background: "#eff1f5",
            TopBarBg: "#e6e9ef",
            TopBarFg: "#4c4f69",
            TopBarAccent: "#1e66f5"),

        new("Tokyo Night",
            BorderActive: "#7aa2f7",
            BorderInactive: "#3b4261",
            Background: "#1a1b26",
            TopBarBg: "#16161e",
            TopBarFg: "#c0caf5",
            TopBarAccent: "#7dcfff"),

        new("Tokyo Night Storm",
            BorderActive: "#bb9af7",
            BorderInactive: "#3b4261",
            Background: "#24283b",
            TopBarBg: "#1f2335",
            TopBarFg: "#c0caf5",
            TopBarAccent: "#7aa2f7"),

        new("Gruvbox Dark",
            BorderActive: "#fe8019",
            BorderInactive: "#504945",
            Background: "#282828",
            TopBarBg: "#1d2021",
            TopBarFg: "#ebdbb2",
            TopBarAccent: "#83a598"),

        new("Gruvbox Light",
            BorderActive: "#af3a03",
            BorderInactive: "#bdae93",
            Background: "#fbf1c7",
            TopBarBg: "#f2e5bc",
            TopBarFg: "#3c3836",
            TopBarAccent: "#427b58"),

        new("Nord",
            BorderActive: "#88c0d0",
            BorderInactive: "#4c566a",
            Background: "#2e3440",
            TopBarBg: "#272c36",
            TopBarFg: "#d8dee9",
            TopBarAccent: "#81a1c1"),

        new("Dracula",
            BorderActive: "#bd93f9",
            BorderInactive: "#44475a",
            Background: "#282a36",
            TopBarBg: "#21222c",
            TopBarFg: "#f8f8f2",
            TopBarAccent: "#ff79c6"),

        new("Rosé Pine",
            BorderActive: "#c4a7e7",
            BorderInactive: "#524f67",
            Background: "#191724",
            TopBarBg: "#1f1d2e",
            TopBarFg: "#e0def4",
            TopBarAccent: "#ebbcba"),

        new("Rosé Pine Moon",
            BorderActive: "#c4a7e7",
            BorderInactive: "#44415a",
            Background: "#232136",
            TopBarBg: "#2a273f",
            TopBarFg: "#e0def4",
            TopBarAccent: "#ea9a97"),

        new("One Dark",
            BorderActive: "#61afef",
            BorderInactive: "#4b5263",
            Background: "#282c34",
            TopBarBg: "#21252b",
            TopBarFg: "#abb2bf",
            TopBarAccent: "#c678dd"),

        new("Solarized Dark",
            BorderActive: "#268bd2",
            BorderInactive: "#073642",
            Background: "#002b36",
            TopBarBg: "#001e26",
            TopBarFg: "#839496",
            TopBarAccent: "#2aa198"),

        new("Everforest Dark",
            BorderActive: "#a7c080",
            BorderInactive: "#4f5b58",
            Background: "#2d353b",
            TopBarBg: "#272e33",
            TopBarFg: "#d3c6aa",
            TopBarAccent: "#7fbbb3"),

        new("Kanagawa",
            BorderActive: "#957fb8",
            BorderInactive: "#54546d",
            Background: "#1f1f28",
            TopBarBg: "#16161d",
            TopBarFg: "#dcd7ba",
            TopBarAccent: "#7e9cd8"),

        new("Ayu Dark",
            BorderActive: "#e6b450",
            BorderInactive: "#3e4b59",
            Background: "#0a0e14",
            TopBarBg: "#07090d",
            TopBarFg: "#bfbdb6",
            TopBarAccent: "#39bae6"),

        new("Custom", "", "", "", "", "", ""),
    ];

    public static readonly IconTheme[] IconThemes =
    [
        new("Emoji",
            Cpu: "🖥",
            Gpu: "🎮",
            Ram: "💾",
            VolumeOn: "🔊",
            VolumeMuted: "🔇",
            NetDown: "⬇",
            NetUp: "⬆",
            Temp: "🌡",
            ActiveWorkspace: "●",
            InactiveWorkspace: "○"),

        new("Nerd Font",
            Cpu: "\uf4bc",    // 
            Gpu: "\uf1b2",    // 
            Ram: "\uf2db",    // 
            VolumeOn: "\uf028",  // 
            VolumeMuted: "\uf026", // 
            NetDown: "\uf063",  // 
            NetUp: "\uf062",    // 
            Temp: "\uf2c9",     // 
            ActiveWorkspace: "\uf444",  // 
            InactiveWorkspace: "\uf4c3"), // 

        new("Minimal",
            Cpu: "C:",
            Gpu: "G:",
            Ram: "R:",
            VolumeOn: "♪",
            VolumeMuted: "♪×",
            NetDown: "↓",
            NetUp: "↑",
            Temp: "°",
            ActiveWorkspace: "■",
            InactiveWorkspace: "□"),

        new("Arrows",
            Cpu: "»",
            Gpu: "»",
            Ram: "»",
            VolumeOn: "◉",
            VolumeMuted: "○",
            NetDown: "↓",
            NetUp: "↑",
            Temp: "△",
            ActiveWorkspace: "◆",
            InactiveWorkspace: "◇"),
    ];

    /// <summary>Find a color theme by name, or null if not found.</summary>
    public static ColorTheme? FindColorTheme(string name) =>
        ColorThemes.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Find an icon theme by name, or null if not found.</summary>
    public static IconTheme? FindIconTheme(string name) =>
        IconThemes.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
