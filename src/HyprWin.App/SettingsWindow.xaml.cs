using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using HyprWin.Core;
using HyprWin.Core.Configuration;

namespace HyprWin.App;

/// <summary>
/// Visual settings window for editing HyprWin configuration.
/// Reads current values from the TOML file and writes changes back.
/// The FileSystemWatcher on ConfigManager picks up changes automatically.
/// Only one instance can be open at a time (singleton pattern).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly string _configPath;
    private readonly HyprWinConfig _config;
    private bool _suppressPresetChange;
    private bool _suppressAnimationPresetChange;

    private static SettingsWindow? _instance;

    /// <summary>
    /// Show the settings window. If already open, bring it to front.
    /// </summary>
    public static void ShowSingleton(string configPath, HyprWinConfig config)
    {
        if (_instance is { IsLoaded: true })
        {
            _instance.Activate();
            return;
        }

        _instance = new SettingsWindow(configPath, config);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }

    private SettingsWindow(string configPath, HyprWinConfig config)
    {
        InitializeComponent();
        _configPath = configPath;
        _config = config;

        LoadValues();
    }

    private void LoadValues()
    {
        // General
        TerminalCmd.Text = _config.General.TerminalCommand;
        WorkspaceCount.Text = _config.General.WorkspaceCount.ToString();
        Autostart.IsChecked = _config.General.Autostart;
        foreach (ComboBoxItem item in WorkspaceMode.Items)
        {
            if (item.Content?.ToString() == _config.General.WorkspaceMode)
            {
                WorkspaceMode.SelectedItem = item;
                break;
            }
        }

        // Layout
        GapsInner.Text = _config.Layout.GapsInner.ToString();
        GapsOuter.Text = _config.Layout.GapsOuter.ToString();
        BorderSize.Text = _config.Layout.BorderSize.ToString();
        Rounding.Text = _config.Layout.Rounding.ToString();

        // Theme presets
        _suppressPresetChange = true;
        ThemePresetCombo.Items.Clear();
        foreach (var t in ThemePresets.ColorThemes)
            ThemePresetCombo.Items.Add(new ComboBoxItem { Content = t.Name });
        foreach (ComboBoxItem item in ThemePresetCombo.Items)
        {
            if (item.Content?.ToString() == _config.Theme.ThemePreset)
            {
                ThemePresetCombo.SelectedItem = item;
                break;
            }
        }
        _suppressPresetChange = false;

        // Icon themes
        IconThemeCombo.Items.Clear();
        foreach (var t in ThemePresets.IconThemes)
            IconThemeCombo.Items.Add(new ComboBoxItem { Content = t.Name });
        foreach (ComboBoxItem item in IconThemeCombo.Items)
        {
            if (item.Content?.ToString() == _config.Theme.IconTheme)
            {
                IconThemeCombo.SelectedItem = item;
                break;
            }
        }

        // Theme
        BorderActive.Text = _config.Theme.BorderActive;
        BorderInactive.Text = _config.Theme.BorderInactive;
        TopBarBg.Text = _config.Theme.TopBarBg;
        TopBarFg.Text = _config.Theme.TopBarFg;
        TopBarAccent.Text = _config.Theme.TopBarAccent;

        // Top Bar
        TopBarEnabled.IsChecked = _config.TopBar.Enabled;
        TopBarHeight.Text = _config.TopBar.Height.ToString();
        TopBarFont.Text = _config.TopBar.Font;
        TopBarFontSize.Text = _config.TopBar.FontSize.ToString();
        TopBarModulesRight.Text = string.Join(", ", _config.TopBar.ModulesRight.Modules);
        foreach (ComboBoxItem item in TopBarPosition.Items)
        {
            if (item.Content?.ToString() == _config.TopBar.Position)
            {
                TopBarPosition.SelectedItem = item;
                break;
            }
        }

        // Animations
        _suppressAnimationPresetChange = true;
        AnimPresetCombo.Items.Clear();
        foreach (var p in AnimationPresets.Presets)
            AnimPresetCombo.Items.Add(new ComboBoxItem { Content = p.Name });
        foreach (ComboBoxItem item in AnimPresetCombo.Items)
        {
            if (item.Content?.ToString() == _config.Animations.Preset)
            {
                AnimPresetCombo.SelectedItem = item;
                break;
            }
        }
        _suppressAnimationPresetChange = false;

        AnimEnabled.IsChecked = _config.Animations.Enabled;
        AnimOpenDuration.Text = _config.Animations.WindowOpenDurationMs.ToString();
        AnimCloseDuration.Text = _config.Animations.WindowCloseDurationMs.ToString();
        AnimMoveDuration.Text = _config.Animations.WindowMoveDurationMs.ToString();
        AnimEasing.Text = _config.Animations.Easing;
        SelectComboItemByContent(AnimOpenStyle, _config.Animations.WindowOpenStyle);
        AnimPopinPercent.Text = _config.Animations.PopinPercent.ToString();

        // Gaming
        GamingEnabled.IsChecked = _config.Gaming.Enabled;
        GamingSuspendAnim.IsChecked = _config.Gaming.SuspendAnimations;
        GamingSuspendBorder.IsChecked = _config.Gaming.SuspendBorder;

        // Exclusions
        ExcludedProcesses.Text = string.Join(", ", _config.Exclude.ProcessNames);
        PopupAllowedProcesses.Text = string.Join(", ", _config.Exclude.AllowPopupProcessNames);
        PopupAllowedClasses.Text = string.Join(", ", _config.Exclude.AllowPopupClassNames);
    }

    private void ThemePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetChange) return;
        var selected = (ThemePresetCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (string.IsNullOrEmpty(selected) || selected == "Custom") return;

        var preset = ThemePresets.FindColorTheme(selected);
        if (preset == null) return;

        BorderActive.Text = preset.BorderActive;
        BorderInactive.Text = preset.BorderInactive;
        TopBarBg.Text = preset.TopBarBg;
        TopBarFg.Text = preset.TopBarFg;
        TopBarAccent.Text = preset.TopBarAccent;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                MessageBox.Show("Config file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var toml = File.ReadAllText(_configPath);

            // General
            toml = SetTomlStringValue(toml, "terminal_command", TerminalCmd.Text.Trim());
            toml = SetTomlValue(toml, "workspace_count", WorkspaceCount.Text.Trim());
            var selectedMode = (WorkspaceMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "monitor_bound";
            toml = SetTomlStringValue(toml, "workspace_mode", selectedMode);
            toml = SetTomlValueInSection(toml, "general", "autostart", Autostart.IsChecked == true ? "true" : "false");

            // Layout
            toml = SetTomlValue(toml, "gaps_inner", GapsInner.Text.Trim());
            toml = SetTomlValue(toml, "gaps_outer", GapsOuter.Text.Trim());
            toml = SetTomlValue(toml, "border_size", BorderSize.Text.Trim());
            toml = SetTomlValue(toml, "rounding", Rounding.Text.Trim());

            // Theme
            var selectedPreset = (ThemePresetCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Custom";
            toml = SetTomlStringValue(toml, "theme_preset", selectedPreset);
            toml = SetTomlStringValue(toml, "border_active", BorderActive.Text.Trim());
            toml = SetTomlStringValue(toml, "border_inactive", BorderInactive.Text.Trim());
            toml = SetTomlStringValue(toml, "top_bar_bg", TopBarBg.Text.Trim());
            toml = SetTomlStringValue(toml, "top_bar_fg", TopBarFg.Text.Trim());
            toml = SetTomlStringValue(toml, "top_bar_accent", TopBarAccent.Text.Trim());

            // Icon theme
            var selectedIconTheme = (IconThemeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Emoji";
            toml = SetTomlStringValue(toml, "icon_theme", selectedIconTheme);

            // Top Bar
            toml = SetTomlValueInSection(toml, "top_bar", "enabled", TopBarEnabled.IsChecked == true ? "true" : "false");
            toml = SetTomlValueInSection(toml, "top_bar", "height", TopBarHeight.Text.Trim());
            var selectedPosition = (TopBarPosition.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "top";
            toml = SetTomlStringValue(toml, "position", selectedPosition);
            toml = SetTomlStringValue(toml, "font", TopBarFont.Text.Trim());
            toml = SetTomlValueInSection(toml, "top_bar", "font_size", TopBarFontSize.Text.Trim());

            // Top Bar Right Modules
            var mods = TopBarModulesRight.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .Select(s => $"\"{s}\"");
            toml = SetTomlArrayValueInSection(toml, "top_bar.modules_right", "modules", $"[{string.Join(", ", mods)}]");

            // Animations — use section-aware replacement: "enabled" also exists in [top_bar]
            var selectedAnimPreset = (AnimPresetCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "custom";
            toml = SetTomlStringValueInSection(toml, "animations", "preset", selectedAnimPreset);
            toml = SetTomlValueInSection(toml, "animations", "enabled", AnimEnabled.IsChecked == true ? "true" : "false");
            toml = SetTomlValueInSection(toml, "animations", "window_open_duration_ms", AnimOpenDuration.Text.Trim());
            toml = SetTomlValueInSection(toml, "animations", "window_close_duration_ms", AnimCloseDuration.Text.Trim());
            toml = SetTomlValueInSection(toml, "animations", "window_move_duration_ms", AnimMoveDuration.Text.Trim());
            toml = SetTomlStringValueInSection(toml, "animations", "easing", AnimEasing.Text.Trim());
            var selectedOpenStyle = (AnimOpenStyle.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "popin";
            toml = SetTomlStringValueInSection(toml, "animations", "window_open_style", selectedOpenStyle);
            toml = SetTomlValueInSection(toml, "animations", "popin_percent", AnimPopinPercent.Text.Trim());

            // Gaming
            toml = SetTomlValueInSection(toml, "gaming", "enabled", GamingEnabled.IsChecked == true ? "true" : "false");
            toml = SetTomlValueInSection(toml, "gaming", "suspend_animations", GamingSuspendAnim.IsChecked == true ? "true" : "false");
            toml = SetTomlValueInSection(toml, "gaming", "suspend_border", GamingSuspendBorder.IsChecked == true ? "true" : "false");

            // Exclusions
            var procs = ExcludedProcesses.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .Select(s => $"\"{s}\"");
            var procsArray = $"[{string.Join(", ", procs)}]";
            toml = SetTomlArrayValue(toml, "process_names", procsArray);

            var popupProc = PopupAllowedProcesses.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .Select(s => $"\"{s}\"");
            toml = SetTomlArrayValue(toml, "allow_popup_process_names", $"[{string.Join(", ", popupProc)}]");

            var popupClasses = PopupAllowedClasses.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .Select(s => $"\"{s}\"");
            toml = SetTomlArrayValue(toml, "allow_popup_class_names", $"[{string.Join(", ", popupClasses)}]");

            File.WriteAllText(_configPath, toml);
            Logger.Instance.Info("Settings saved via UI");

            Close();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error saving settings", ex);
            MessageBox.Show($"Failed to save:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenToml_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _configPath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to open config file", ex);
        }
    }

    private static string SetTomlValue(string toml, string key, string value)
    {
        var pattern = $@"^(\s*{Regex.Escape(key)}\s*=\s*)(.+?)(\s*(?:#.*)?)$";
        return Regex.Replace(toml, pattern, "${1}" + value + "${3}", RegexOptions.Multiline);
    }

    private static string SetTomlValueInSection(string toml, string section, string key, string value)
    {
        int start = toml.IndexOf($"\n[{section}]");
        if (start < 0)
        {
            if (!toml.StartsWith($"[{section}]"))
                return SetTomlValue(toml, key, value);
            start = 0;
        }
        else
        {
            start += 1;
        }

        int searchFrom = start + section.Length + 2;
        var nextSection = Regex.Match(toml.Substring(searchFrom), @"^\[(?!\[)", RegexOptions.Multiline);
        int end = nextSection.Success ? searchFrom + nextSection.Index : toml.Length;

        string before = toml[..start];
        string sectionContent = toml[start..end];
        string after = toml[end..];

        sectionContent = SetTomlValue(sectionContent, key, value);
        return before + sectionContent + after;
    }

    private static string SetTomlStringValueInSection(string toml, string section, string key, string value)
    {
        int start = toml.IndexOf($"\n[{section}]");
        if (start < 0)
        {
            if (!toml.StartsWith($"[{section}]"))
                return SetTomlStringValue(toml, key, value);
            start = 0;
        }
        else
        {
            start += 1;
        }

        int searchFrom = start + section.Length + 2;
        var nextSection = Regex.Match(toml.Substring(searchFrom), @"^\[(?!\[)", RegexOptions.Multiline);
        int end = nextSection.Success ? searchFrom + nextSection.Index : toml.Length;

        string before = toml[..start];
        string sectionContent = toml[start..end];
        string after = toml[end..];

        var pattern = $@"^(\s*{Regex.Escape(key)}\s*=\s*)""[^""]*""(.*)$";
        if (Regex.IsMatch(sectionContent, pattern, RegexOptions.Multiline))
            sectionContent = Regex.Replace(sectionContent, pattern, "${1}\"" + value + "\"${2}", RegexOptions.Multiline);
        else
            sectionContent = InsertKeyInSection(sectionContent, section, $"{key} = \"{value}\"");
        return before + sectionContent + after;
    }


    private static string SetTomlStringValue(string toml, string key, string value)
    {
        var pattern = $@"^(\s*{Regex.Escape(key)}\s*=\s*)""[^""]*""(.*)$";
        var result = Regex.Replace(toml, pattern, "${1}\"" + value + "\"${2}", RegexOptions.Multiline);
        if (result == toml && !Regex.IsMatch(toml, $@"^\s*{Regex.Escape(key)}\s*=", RegexOptions.Multiline))
        {
            // Key doesn't exist — insert it after the [theme] section header
            result = InsertKeyInSection(toml, "theme", $"{key} = \"{value}\"");
        }
        return result;
    }

    /// <summary>
    /// Insert a new TOML key=value line right after a [section] header.
    /// </summary>
    private static string InsertKeyInSection(string toml, string section, string line)
    {
        var headerPattern = $@"^(\[{Regex.Escape(section)}\]\s*(?:#.*)?)$";
        var match = Regex.Match(toml, headerPattern, RegexOptions.Multiline);
        if (!match.Success) return toml;

        int insertPos = match.Index + match.Length;
        return toml[..insertPos] + "\n" + line + toml[insertPos..];
    }

    private static string SetTomlArrayValue(string toml, string key, string arrayValue)
    {
        var pattern = $@"^(\s*{Regex.Escape(key)}\s*=\s*)\[.*?\](.*)$";
        return Regex.Replace(toml, pattern, "${1}" + arrayValue + "${2}", RegexOptions.Multiline);
    }

    private static string SetTomlArrayValueInSection(string toml, string section, string key, string arrayValue)
    {
        int start = toml.IndexOf($"\n[{section}]");
        if (start < 0) return SetTomlArrayValue(toml, key, arrayValue);
        start += 1;

        int searchFrom = start + section.Length + 2;
        var nextSection = Regex.Match(toml.Substring(searchFrom), @"^\[(?!\[)", RegexOptions.Multiline);
        int end = nextSection.Success ? searchFrom + nextSection.Index : toml.Length;

        string before = toml[..start];
        string sectionContent = toml[start..end];
        string after = toml[end..];

        sectionContent = SetTomlArrayValue(sectionContent, key, arrayValue);
        return before + sectionContent + after;
    }

    private void AnimPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAnimationPresetChange) return;
        var selected = (AnimPresetCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (string.IsNullOrWhiteSpace(selected) || selected.Equals("custom", StringComparison.OrdinalIgnoreCase))
            return;

        var preset = AnimationPresets.Find(selected);
        AnimOpenDuration.Text = preset.OpenDurationMs.ToString();
        AnimCloseDuration.Text = preset.CloseDurationMs.ToString();
        AnimMoveDuration.Text = preset.MoveDurationMs.ToString();
        AnimEasing.Text = preset.Easing;
        SelectComboItemByContent(AnimOpenStyle, preset.WindowOpenStyle);
        AnimPopinPercent.Text = preset.PopinPercent.ToString();
    }

    private static void SelectComboItemByContent(ComboBox combo, string value)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }
}
