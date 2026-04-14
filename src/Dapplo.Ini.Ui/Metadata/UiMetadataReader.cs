// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reflection;
using Dapplo.Ini.Ui.Attributes;
using Dapplo.Ini.Ui.Enums;

namespace Dapplo.Ini.Ui.Metadata;

/// <summary>
/// Metadata for a single configuration property derived from its UI attributes.
/// </summary>
public sealed class UiPropertyMetadata
{
    /// <summary>Gets the CLR property name on the section interface.</summary>
    public string PropertyName { get; init; } = "";

    /// <summary>Gets the UI control type to use for this property.</summary>
    public UiControlType ControlType { get; init; } = UiControlType.TextBox;

    /// <summary>Gets the <see cref="UiControlAttribute"/> attached to this property, if any.</summary>
    public UiControlAttribute? ControlAttribute { get; init; }

    /// <summary>Gets the group name this property belongs to, or <c>null</c>.</summary>
    public string? GroupName { get; init; }

    /// <summary>Gets the <see cref="UiGroupAttribute"/> attached to this property, if any.</summary>
    public UiGroupAttribute? GroupAttribute { get; init; }

    /// <summary>Gets the display order within its group or page.</summary>
    public int Order { get; init; }

    /// <summary>Gets the change mode for this property.</summary>
    public UiChangeMode ChangeMode { get; init; } = UiChangeMode.Immediate;

    /// <summary>Gets the i18n label key, or <c>null</c> if the property name should be used directly.</summary>
    public string? LabelKey { get; init; }

    /// <summary>Gets the i18n description/tooltip key, or <c>null</c>.</summary>
    public string? DescriptionKey { get; init; }

    /// <summary>Gets the name of the <c>bool</c> property that controls this property's visibility, or <c>null</c>.</summary>
    public string? VisibilityConditionProperty { get; init; }

    /// <summary>Gets whether the visibility condition is inverted.</summary>
    public bool InvertVisibility { get; init; }

    /// <summary>Gets the name of the <c>bool</c> property that controls whether this control is enabled, or <c>null</c>.</summary>
    public string? EnableConditionProperty { get; init; }

    /// <summary>Gets whether the enable condition is inverted.</summary>
    public bool InvertEnable { get; init; }
}

/// <summary>
/// Metadata for a settings page (one INI section interface) derived from its UI attributes.
/// </summary>
public sealed class UiPageMetadata
{
    /// <summary>Gets the section interface <see cref="Type"/>.</summary>
    public Type SectionType { get; init; } = typeof(object);

    /// <summary>Gets the page title used in the settings window navigation.</summary>
    public string Title { get; init; } = "";

    /// <summary>Gets the optional category / parent node for tree-style navigation.</summary>
    public string? Category { get; init; }

    /// <summary>Gets the display order of this page in the navigation.</summary>
    public int Order { get; init; }

    /// <summary>Gets an optional icon resource key.</summary>
    public string? Icon { get; init; }

    /// <summary>Gets the default change mode for all properties in this section.</summary>
    public UiChangeMode DefaultChangeMode { get; init; } = UiChangeMode.Immediate;

    /// <summary>Gets the metadata for all UI-visible properties in this section, ordered for display.</summary>
    public IReadOnlyList<UiPropertyMetadata> Properties { get; init; } = [];
}

/// <summary>
/// Reads UI attribute metadata from INI section interface types using reflection.
/// </summary>
/// <remarks>
/// This class is intentionally simple — it relies on attributes applied directly to
/// the interface and its properties.  Source generators can produce equivalent metadata
/// at compile time instead of using this runtime reflection helper.
/// </remarks>
public static class UiMetadataReader
{
    /// <summary>
    /// Reads and returns the <see cref="UiPageMetadata"/> for the given INI section interface.
    /// </summary>
    /// <typeparam name="T">An interface type that extends <c>IIniSection</c>.</typeparam>
    /// <returns>Metadata describing the page and all its UI-visible properties.</returns>
    public static UiPageMetadata ReadPage<T>() => ReadPage(typeof(T));

    /// <summary>
    /// Reads and returns the <see cref="UiPageMetadata"/> for the given INI section interface type.
    /// </summary>
    /// <param name="sectionType">An interface type that extends <c>IIniSection</c>.</param>
    /// <returns>Metadata describing the page and all its UI-visible properties.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sectionType"/> is <c>null</c>.</exception>
    public static UiPageMetadata ReadPage(Type sectionType)
    {
        if (sectionType is null) throw new ArgumentNullException(nameof(sectionType));

        var pageAttr = sectionType.GetCustomAttribute<UiPageAttribute>();
        var changeModeAttr = sectionType.GetCustomAttribute<UiChangeModeAttribute>();
        var labelKeyAttr = sectionType.GetCustomAttribute<UiLabelKeyAttribute>();

        // Derive the default title from the interface name (strip leading 'I').
        var defaultTitle = sectionType.Name.StartsWith("I", StringComparison.Ordinal) && sectionType.Name.Length > 1
            ? sectionType.Name[1..]
            : sectionType.Name;

        var sectionDefaultChangeMode = changeModeAttr?.ChangeMode ?? UiChangeMode.Immediate;

        var props = new List<UiPropertyMetadata>();
        foreach (var prop in sectionType.GetProperties())
        {
            var controlAttr = prop.GetCustomAttribute<UiControlAttribute>();
            var groupAttr = prop.GetCustomAttribute<UiGroupAttribute>();
            var orderAttr = prop.GetCustomAttribute<UiOrderAttribute>();
            var propChangeModeAttr = prop.GetCustomAttribute<UiChangeModeAttribute>();
            var propLabelKeyAttr = prop.GetCustomAttribute<UiLabelKeyAttribute>();
            var visibilityAttr = prop.GetCustomAttribute<UiConditionalVisibilityAttribute>();
            var enableAttr = prop.GetCustomAttribute<UiConditionalEnableAttribute>();

            var controlType = controlAttr?.ControlType ?? InferControlType(prop.PropertyType);

            props.Add(new UiPropertyMetadata
            {
                PropertyName = prop.Name,
                ControlType = controlType,
                ControlAttribute = controlAttr,
                GroupName = groupAttr?.GroupName,
                GroupAttribute = groupAttr,
                Order = orderAttr?.Order ?? groupAttr?.Order ?? 0,
                ChangeMode = propChangeModeAttr?.ChangeMode ?? sectionDefaultChangeMode,
                LabelKey = propLabelKeyAttr?.LabelKey,
                DescriptionKey = propLabelKeyAttr?.DescriptionKey,
                VisibilityConditionProperty = visibilityAttr?.ConditionProperty,
                InvertVisibility = visibilityAttr?.Invert ?? false,
                EnableConditionProperty = enableAttr?.ConditionProperty,
                InvertEnable = enableAttr?.Invert ?? false,
            });
        }

        // Sort by order, preserving original declaration order for ties.
        props.Sort((a, b) => a.Order.CompareTo(b.Order));

        return new UiPageMetadata
        {
            SectionType = sectionType,
            Title = pageAttr?.Title ?? labelKeyAttr?.LabelKey ?? defaultTitle,
            Category = pageAttr?.Category,
            Order = pageAttr?.Order ?? 0,
            Icon = pageAttr?.Icon,
            DefaultChangeMode = sectionDefaultChangeMode,
            Properties = props,
        };
    }

    /// <summary>
    /// Infers the most appropriate <see cref="UiControlType"/> for a given CLR property type
    /// when no explicit <see cref="UiControlAttribute"/> is present.
    /// </summary>
    public static UiControlType InferControlType(Type propertyType)
    {
        if (propertyType is null) throw new ArgumentNullException(nameof(propertyType));

        // Unwrap Nullable<T>
        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (underlying == typeof(bool)) return UiControlType.CheckBox;
        if (underlying.IsEnum) return UiControlType.DropDown;
        if (underlying == typeof(string)) return UiControlType.TextBox;
        if (underlying == typeof(int) || underlying == typeof(long) ||
            underlying == typeof(short) || underlying == typeof(byte) ||
            underlying == typeof(uint) || underlying == typeof(ulong) ||
            underlying == typeof(ushort) || underlying == typeof(float) ||
            underlying == typeof(double) || underlying == typeof(decimal))
        {
            return UiControlType.UpDown;
        }

        return UiControlType.TextBox;
    }
}
