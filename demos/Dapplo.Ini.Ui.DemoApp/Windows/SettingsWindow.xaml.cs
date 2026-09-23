// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Windows;
using System.Windows.Controls;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Ui.Configuration;
using Dapplo.Ini.Ui.DemoApp.Configuration;
using Dapplo.Ini.Ui.DemoApp.Renderer;
using Dapplo.Ini.Ui.Metadata;

namespace Dapplo.Ini.Ui.DemoApp.Windows;

/// <summary>
/// Settings dialog.
/// </summary>
/// <remarks>
/// <para>
/// The window is driven entirely by the compile-time UI descriptors produced by the
/// <c>Dapplo.Ini.Ui.Generator</c> source generator.  Each registered section is
/// represented by a tab whose content is built dynamically by
/// <see cref="WpfSettingsRenderer.BuildPage"/>.
/// </para>
/// <para>
/// Session lifecycle is managed by <see cref="UiSettingsManager"/>:
/// <list type="bullet">
///   <item><description><c>BeginSession()</c> is called in the constructor → records rollback snapshot.</description></item>
///   <item><description>OK → <c>Apply()</c> → commits changes, then closes.</description></item>
///   <item><description>Cancel / close → <c>EndSession()</c> → auto-rolls back any pending changes.</description></item>
///   <item><description>Apply button → <c>Apply()</c> → commits changes, window stays open.</description></item>
/// </list>
/// </para>
/// </remarks>
public partial class SettingsWindow : Window
{
    private readonly UiSettingsManager _manager = new();

    // Guards against EndSession() being called twice when OK/Cancel sets DialogResult
    // (which triggers OnClosing) after already ending the session explicitly.
    private bool _sessionEnded;

    /// <summary>
    /// Initialises the settings window and builds all tabs from the provided pages.
    /// </summary>
    /// <param name="pages">
    /// Sequence of (compile-time descriptor, live section) pairs to render.
    /// Typically sourced from the <c>*UiDescriptor.Page</c> static properties emitted
    /// by <c>Dapplo.Ini.Ui.Generator</c>.
    /// </param>
    public SettingsWindow(IEnumerable<(UiPageMetadata Page, IIniSection Section)> pages)
    {
        InitializeComponent();

        foreach (var (page, section) in pages.OrderBy(p => p.Page.Order))
        {
            _manager.RegisterSection(section);

            // Category support: pages that share the same Category are placed under
            // a parent tab.  For simplicity this demo renders them as flat tabs with
            // the category shown in the header.
            var header = page.Category != null ? $"{page.Category} › {page.Title}" : page.Title;

            var tab = new TabItem
            {
                Header = header,
                // WpfSettingsRenderer builds the complete scrollable panel for the page.
                Content = WpfSettingsRenderer.BuildPage(page, section),
            };

            PagesTabControl.Items.Add(tab);
        }

        // Select the first tab by default.
        if (PagesTabControl.Items.Count > 0)
            PagesTabControl.SelectedIndex = 0;

        // Begin the session *after* all controls are built so the initial state
        // is already visible in the UI when the rollback snapshot is taken.
        _manager.BeginSession();
    }

    // ── Session helpers ────────────────────────────────────────────────────────

    /// <summary>Ends the session at most once; safe to call from both button handlers and OnClosing.</summary>
    private void EndSessionOnce()
    {
        if (_sessionEnded) return;
        _sessionEnded = true;
        _manager.EndSession();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _manager.Apply();
        EndSessionOnce();   // properly terminates the session (no rollback — nothing pending after Apply)
        DialogResult = true;    // signals the caller that the user confirmed
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        EndSessionOnce();       // rolls back pending changes
        DialogResult = false;
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _manager.Apply();
        // Keep the window open so the user can continue editing.
    }

    /// <summary>
    /// Titlebar × close button — treated as Cancel (rolls back pending changes).
    /// </summary>
    private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    /// <summary>Roll back on window close (titlebar ×, Alt+F4) unless already handled.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        EndSessionOnce();
    }
}
