// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Windows;

namespace Dapplo.Ini.Ui.DemoApp.Theme;

/// <summary>
/// Switches the application between dark and light themes at runtime by replacing
/// the last <see cref="ResourceDictionary"/> in
/// <see cref="Application.Current"/> merged dictionaries.
/// </summary>
/// <remarks>
/// Call <see cref="Apply"/> once during startup (before the main window is shown) and
/// again whenever <c>IAppearanceSettings.DarkMode</c> changes.  Because the theme
/// dictionary is the last entry in the merged-dictionary list, replacing it forces WPF
/// to re-evaluate every <c>DynamicResource</c> reference in all live windows.
/// </remarks>
internal static class ThemeManager
{
    private const string LightThemeUri = "Themes/LightTheme.xaml";
    private const string DarkThemeUri  = "Themes/DarkTheme.xaml";

    /// <summary>
    /// Applies the dark or light theme to the running application.
    /// </summary>
    /// <param name="darkMode"><c>true</c> to switch to the dark theme; <c>false</c> for the light theme.</param>
    public static void Apply(bool darkMode)
    {
        var uri  = new Uri(darkMode ? DarkThemeUri : LightThemeUri, UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;

        // The last entry in MergedDictionaries is always the active theme.
        // Replace it so WPF fires DynamicResource change notifications for all
        // windows that reference keys defined in the theme dictionary.
        if (merged.Count > 0)
            merged[^1] = dict;
        else
            merged.Add(dict);
    }
}
