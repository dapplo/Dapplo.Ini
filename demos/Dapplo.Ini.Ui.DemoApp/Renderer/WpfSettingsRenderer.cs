// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Ui.Enums;
using Dapplo.Ini.Ui.Metadata;

namespace Dapplo.Ini.Ui.DemoApp.Renderer;

/// <summary>
/// Dynamically builds WPF UI controls from a <see cref="UiPageMetadata"/> descriptor
/// and a live <see cref="IIniSection"/> instance — no hand-written XAML required.
/// </summary>
/// <remarks>
/// <para>
/// <b>How it works</b>
/// <list type="number">
///   <item>
///     <description>
///       <see cref="BuildPage"/> is called with the compile-time descriptor produced by
///       <c>Dapplo.Ini.Ui.Generator</c> and the live section instance that holds the
///       current values.
///     </description>
///   </item>
///   <item>
///     <description>
///       Properties that share a <c>GroupName</c> are placed inside a
///       <see cref="GroupBox"/>.  Ungrouped properties are placed directly in the
///       outer <see cref="StackPanel"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       Each property is rendered as a two-column row (label | control).  The control
///       type is taken from <see cref="UiPropertyMetadata.ControlType"/> which was set
///       by the source generator (explicit attribute) or inferred from the CLR type.
///     </description>
///   </item>
///   <item>
///     <description>
///       Value changes are written back to the section via the compiled
///       <see cref="UiPropertyMetadata.Setter"/> delegate — no <c>PropertyInfo</c>
///       reflection at the call site.  When the section implements
///       <see cref="INotifyPropertyChanged"/> the renderer subscribes to
///       <c>PropertyChanged</c> and re-evaluates all conditional visibility / enable
///       rules automatically.
///     </description>
///   </item>
/// </list>
/// </para>
/// </remarks>
public static class WpfSettingsRenderer
{
    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the complete scrollable UI panel for a settings page.
    /// </summary>
    /// <param name="page">
    /// The compile-time <see cref="UiPageMetadata"/> descriptor (typically
    /// <c>MySettingsUiDescriptor.Page</c>) or the runtime equivalent from
    /// <see cref="UiMetadataReader.ReadPage{T}"/>.
    /// </param>
    /// <param name="section">
    /// The live <see cref="IIniSection"/> instance whose properties will be read and
    /// written by the generated controls.
    /// </param>
    /// <returns>A <see cref="ScrollViewer"/> ready to be hosted in a <see cref="TabItem"/>.</returns>
    public static UIElement BuildPage(UiPageMetadata page, IIniSection section)
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var outerPanel = new StackPanel { Margin = new Thickness(12) };
        scrollViewer.Content = outerPanel;

        // Build a name→getter lookup for fast condition-property resolution.
        // Both source-generated and UiMetadataReader-created metadata always populate Getter.
        var gettersByName = page.Properties
            .Where(p => p.Getter != null)
            .ToDictionary(p => p.PropertyName, p => p.Getter!);

        // Collect every (row element, metadata) pair so that condition evaluation can
        // show/hide or enable/disable them later.
        var rows = new List<(FrameworkElement Element, UiPropertyMetadata Meta)>();

        // Group properties: null key → top level; otherwise → GroupBox.
        // Groups are ordered by the minimum Order of their member properties.
        var groups = page.Properties
            .GroupBy(p => p.GroupName)
            .OrderBy(g => g.Key == null ? int.MinValue : g.Min(p => p.Order));

        foreach (var group in groups)
        {
            Panel targetPanel;

            if (group.Key != null)
            {
                var groupBox = new GroupBox
                {
                    Header = group.Key,
                    Margin = new Thickness(0, 10, 0, 0),
                    Padding = new Thickness(8, 4, 8, 8),
                };
                var inner = new StackPanel();
                groupBox.Content = inner;
                outerPanel.Children.Add(groupBox);
                targetPanel = inner;
            }
            else
            {
                targetPanel = outerPanel;
            }

            foreach (var prop in group.OrderBy(p => p.Order))
            {
                // Capture for the closure
                var capturedRows = rows;
                var capturedGetters = gettersByName;
                var capturedSection = section;
                void Reevaluate() => EvaluateAllConditions(capturedRows, capturedGetters, capturedSection);

                var row = BuildRow(prop, section, Reevaluate);
                targetPanel.Children.Add(row);
                rows.Add((row, prop));
            }
        }

        // Subscribe to section's INotifyPropertyChanged (if supported) so conditions
        // are re-evaluated reactively when the section fires PropertyChanged.
        if (section is INotifyPropertyChanged npc)
            npc.PropertyChanged += (_, _) => EvaluateAllConditions(rows, gettersByName, section);

        // Run initial condition pass so controls start in the correct visible/enabled state.
        EvaluateAllConditions(rows, gettersByName, section);

        return scrollViewer;
    }

    // ── Condition evaluation ──────────────────────────────────────────────────

    private static void EvaluateAllConditions(
        List<(FrameworkElement Element, UiPropertyMetadata Meta)> rows,
        Dictionary<string, Func<IIniSection, object?>> gettersByName,
        IIniSection section)
    {
        foreach (var (element, meta) in rows)
        {
            // ── Visibility condition ──────────────────────────────────────────
            if (meta.VisibilityConditionProperty != null)
            {
                bool condVal = false;
                if (gettersByName.TryGetValue(meta.VisibilityConditionProperty, out var getter))
                {
                    try { condVal = getter(section) is true; }
                    catch { /* getter failure → treat condition as false (control stays hidden) */ }
                }
                if (meta.InvertVisibility) condVal = !condVal;
                element.Visibility = condVal ? Visibility.Visible : Visibility.Collapsed;
            }

            // ── Enable condition ──────────────────────────────────────────────
            if (meta.EnableConditionProperty != null)
            {
                bool condVal = false;
                if (gettersByName.TryGetValue(meta.EnableConditionProperty, out var getter))
                {
                    try { condVal = getter(section) is true; }
                    catch { /* getter failure → treat condition as false (control stays disabled) */ }
                }
                if (meta.InvertEnable) condVal = !condVal;
                // Disable all interactive children in the row (skip label).
                SetChildrenEnabled(element, condVal);
            }
        }
    }

    private static void SetChildrenEnabled(FrameworkElement element, bool enabled)
    {
        if (element is Grid grid)
        {
            // Column 0 is always the label — only disable column 1 (the input control).
            foreach (UIElement child in grid.Children)
                if (Grid.GetColumn(child) == 1 && child is FrameworkElement fe)
                    fe.IsEnabled = enabled;
        }
        else
        {
            element.IsEnabled = enabled;
        }
    }

    // ── Row builder ───────────────────────────────────────────────────────────

    private static FrameworkElement BuildRow(
        UiPropertyMetadata meta,
        IIniSection section,
        Action reevaluate)
    {
        var labelText = meta.LabelKey != null ? SplitCamelCase(meta.LabelKey) : SplitCamelCase(meta.PropertyName);
        var currentValue = meta.Getter?.Invoke(section);

        // CheckBox is self-labelled: no extra label column needed.
        if (meta.ControlType == UiControlType.CheckBox)
        {
            var cb = new CheckBox
            {
                Content = labelText,
                IsChecked = currentValue is true,
                Margin = new Thickness(0, 6, 0, 0),
            };
            cb.Checked   += (_, _) => { meta.Setter?.Invoke(section, true);  reevaluate(); };
            cb.Unchecked += (_, _) => { meta.Setter?.Invoke(section, false); reevaluate(); };
            return cb;
        }

        // All other controls: two-column Grid [Label | Control]
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = labelText + ":",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        FrameworkElement control = meta.ControlType switch
        {
            UiControlType.DropDown     => BuildComboBox(meta, currentValue, section, reevaluate),
            UiControlType.Slider       => BuildSlider(meta, currentValue, section, reevaluate),
            UiControlType.UpDown       => BuildUpDown(meta, currentValue, section, reevaluate),
            UiControlType.FolderPicker => BuildFolderPicker(meta, currentValue, section, reevaluate),
            UiControlType.FilePicker   => BuildFilePicker(meta, currentValue, section, reevaluate),
            _                          => BuildTextBox(meta, currentValue, section, reevaluate),
        };
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        return grid;
    }

    // ── Individual control builders ───────────────────────────────────────────

    private static TextBox BuildTextBox(
        UiPropertyMetadata meta, object? value,
        IIniSection section, Action reevaluate)
    {
        var tb = new TextBox
        {
            Text = value?.ToString() ?? "",
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (meta.ControlAttribute?.Placeholder != null)
            tb.ToolTip = meta.ControlAttribute.Placeholder;

        tb.TextChanged += (_, _) =>
        {
            meta.Setter?.Invoke(section, tb.Text);
            reevaluate();
        };
        return tb;
    }

    private static ComboBox BuildComboBox(
        UiPropertyMetadata meta, object? value,
        IIniSection section, Action reevaluate)
    {
        var cb = new ComboBox { VerticalAlignment = VerticalAlignment.Center };

        // EnumNames is populated at compile time by the source generator, or at
        // startup by UiMetadataReader — no PropertyInfo reflection needed here.
        if (meta.EnumNames != null)
            cb.ItemsSource = meta.EnumNames;

        cb.SelectedItem = value?.ToString();

        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is string selected)
            {
                meta.Setter?.Invoke(section, selected);
                reevaluate();
            }
        };
        return cb;
    }

    private static FrameworkElement BuildSlider(
        UiPropertyMetadata meta, object? value,
        IIniSection section, Action reevaluate)
    {
        var ctrl = meta.ControlAttribute;
        double min  = ctrl?.Minimum ?? 0;
        double max  = ctrl?.Maximum ?? 100;
        double step = ctrl?.Increment ?? 1;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Convert.ToDouble(value ?? min),
            Width = 220,
            VerticalAlignment = VerticalAlignment.Center,
            TickFrequency = step,
            IsSnapToTickEnabled = step >= 1,
            LargeChange = step * 5,
            SmallChange = step,
        };

        var valueDisplay = new TextBlock
        {
            Text = FormatValue(value, ctrl?.Unit),
            Width = 50,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        slider.ValueChanged += (_, e) =>
        {
            var newVal = (int)Math.Round(e.NewValue, ctrl?.DecimalPlaces ?? 0);
            valueDisplay.Text = FormatValue(newVal, ctrl?.Unit);
            meta.Setter?.Invoke(section, newVal);
            reevaluate();
        };

        panel.Children.Add(slider);
        panel.Children.Add(valueDisplay);
        return panel;
    }

    private static FrameworkElement BuildUpDown(
        UiPropertyMetadata meta, object? value,
        IIniSection section, Action reevaluate)
    {
        var ctrl = meta.ControlAttribute;
        double min  = ctrl?.Minimum ?? double.MinValue;
        double max  = ctrl?.Maximum ?? double.MaxValue;
        double step = ctrl?.Increment ?? 1;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        var tb = new TextBox
        {
            Text = value?.ToString() ?? "0",
            Width = 80,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
        };

        var upBtn   = new RepeatButton { Content = "▲", Width = 22, Height = 13, FontSize = 8, Padding = new Thickness(0), Focusable = false };
        var downBtn = new RepeatButton { Content = "▼", Width = 22, Height = 13, FontSize = 8, Padding = new Thickness(0), Focusable = false };
        var btnStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        btnStack.Children.Add(upBtn);
        btnStack.Children.Add(downBtn);

        void Commit()
        {
            if (double.TryParse(tb.Text, out var v))
            {
                meta.Setter?.Invoke(section, (int)Math.Clamp(v, min, max));
                reevaluate();
            }
        }

        tb.LostFocus += (_, _) => Commit();
        upBtn.Click += (_, _) =>
        {
            if (double.TryParse(tb.Text, out var v))
                tb.Text = ((int)Math.Clamp(v + step, min, max)).ToString();
            Commit();
        };
        downBtn.Click += (_, _) =>
        {
            if (double.TryParse(tb.Text, out var v))
                tb.Text = ((int)Math.Clamp(v - step, min, max)).ToString();
            Commit();
        };

        panel.Children.Add(tb);
        panel.Children.Add(btnStack);

        if (ctrl?.Unit != null)
            panel.Children.Add(new TextBlock
            {
                Text = ctrl.Unit,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

        return panel;
    }

    private static FrameworkElement BuildFolderPicker(
        UiPropertyMetadata meta, object? value,
        IIniSection section, Action reevaluate)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var tb = new TextBox { Text = value?.ToString() ?? "", Width = 200, VerticalAlignment = VerticalAlignment.Center };
        var btn = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0) };

        tb.TextChanged += (_, _) => { meta.Setter?.Invoke(section, tb.Text); reevaluate(); };

        btn.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = meta.LabelKey ?? meta.PropertyName,
                InitialDirectory = tb.Text,
            };
            if (dialog.ShowDialog() == true)
                tb.Text = dialog.FolderName;
        };

        panel.Children.Add(tb);
        panel.Children.Add(btn);
        return panel;
    }

    private static FrameworkElement BuildFilePicker(
        UiPropertyMetadata meta, object? value,
        IIniSection section, Action reevaluate)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var tb = new TextBox { Text = value?.ToString() ?? "", Width = 200, VerticalAlignment = VerticalAlignment.Center };
        var btn = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0) };

        tb.TextChanged += (_, _) => { meta.Setter?.Invoke(section, tb.Text); reevaluate(); };

        btn.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = meta.LabelKey ?? meta.PropertyName,
                FileName = tb.Text,
            };
            if (dialog.ShowDialog() == true)
                tb.Text = dialog.FileName;
        };

        panel.Children.Add(tb);
        panel.Children.Add(btn);
        return panel;
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static string FormatValue(object? value, string? unit)
        => unit == null ? value?.ToString() ?? "" : $"{value} {unit}";

    /// <summary>Converts an i18n key like <c>general_app_name</c> to <c>App Name</c>.</summary>
    private static string SplitCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // i18n key format: "section_property_name" → "Property Name"
        if (input.Contains('_'))
        {
            var parts = input.Split('_');
            // Skip the first token if it looks like a section prefix (all lower-case)
            var words = parts.Length > 1 && parts[0].All(char.IsLower)
                ? parts.Skip(1)
                : parts;
            return string.Join(" ", words.Select(w => char.ToUpper(w[0]) + w[1..]));
        }

        // CamelCase property name → "Camel Case"
        var sb = new StringBuilder();
        foreach (var c in input)
        {
            if (char.IsUpper(c) && sb.Length > 0)
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
