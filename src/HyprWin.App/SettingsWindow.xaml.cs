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
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly string _configPath;
    private readonly HyprWinConfig _config;

    public SettingsWindow(string configPath, HyprWinConfig config)
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
        AnimEnabled.IsChecked = _config.Animations.Enabled;
        AnimMoveDuration.Text = _config.Animations.WindowMoveDurationMs.ToString();

        // Gaming
        GamingEnabled.IsChecked = _config.Gaming.Enabled;
        GamingSuspendAnim.IsChecked = _config.Gaming.SuspendAnimations;
        GamingSuspendBorder.IsChecked = _config.Gaming.SuspendBorder;

        // Exclusions
        ExcludedProcesses.Text = string.Join(", ", _config.Exclude.ProcessNames);
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
            toml = SetTomlStringValue(toml, "border_active", BorderActive.Text.Trim());
            toml = SetTomlStringValue(toml, "border_inactive", BorderInactive.Text.Trim());
            toml = SetTomlStringValue(toml, "top_bar_bg", TopBarBg.Text.Trim());
            toml = SetTomlStringValue(toml, "top_bar_fg", TopBarFg.Text.Trim());
            toml = SetTomlStringValue(toml, "top_bar_accent", TopBarAccent.Text.Trim());

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

            // Animations
            toml = SetTomlValueInSection(toml, "animations", "enabled", AnimEnabled.IsChecked == true ? "true" : "false");
            toml = SetTomlValue(toml, "window_move_duration_ms", AnimMoveDuration.Text.Trim());

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

    private static string SetTomlStringValue(string toml, string key, string value)
    {
        var pattern = $@"^(\s*{Regex.Escape(key)}\s*=\s*)""[^""]*""(.*)$";
        return Regex.Replace(toml, pattern, "${1}\"" + value + "\"${2}", RegexOptions.Multiline);
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
}
