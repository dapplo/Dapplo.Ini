// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Configuration;

/// <summary>Where the current value of a key came from, see <see cref="IIniSection.GetValueOrigin"/>.</summary>
public enum ValueOriginKind
{
    /// <summary>The compiled default (<c>[DefaultValue]</c> / <c>[IniValue(DefaultValue = …)]</c>) of the property.</summary>
    CompiledDefault,
    /// <summary>A defaults file registered with <see cref="IniConfigBuilder.AddDefaultsFile"/>.</summary>
    DefaultsFile,
    /// <summary>The user file, or a value the application set after the load.</summary>
    UserFile,
    /// <summary>A constants file registered with <see cref="IniConfigBuilder.AddConstantsFile"/>.</summary>
    ConstantsFile,
    /// <summary>An external value source registered with <see cref="IniConfigBuilder.AddValueSource(IValueSource, ValueSourceLayer)"/>.</summary>
    ValueSource
}

/// <summary>
/// Describes where the current value of a key came from: returned by <see cref="IIniSection.GetValueOrigin"/>.
/// </summary>
public sealed class ValueOrigin : IEquatable<ValueOrigin>
{
    /// <summary>The value is the compiled default of the property.</summary>
    public static readonly ValueOrigin CompiledDefault = new(ValueOriginKind.CompiledDefault, null, null, null);

    /// <summary>The value was set by the application after the load (it is written to the user file on save).</summary>
    public static readonly ValueOrigin Application = new(ValueOriginKind.UserFile, null, null, null);

    internal ValueOrigin(ValueOriginKind kind, string? filePath, object? source, ValueSourceLayer? layer)
    {
        Kind = kind;
        FilePath = filePath;
        Source = source;
        Layer = layer;
        SourceName = source switch
        {
            null => null,
            INamedValueSource named => named.Name,
            _ => source.GetType().Name
        };
    }

    /// <summary>The kind of layer that supplied the value.</summary>
    public ValueOriginKind Kind { get; }

    /// <summary>
    /// The path of the file that supplied the value, for <see cref="ValueOriginKind.DefaultsFile"/>,
    /// <see cref="ValueOriginKind.UserFile"/> and <see cref="ValueOriginKind.ConstantsFile"/>.
    /// <c>null</c> for a value the application set after the load.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// The <see cref="IValueSource"/> or <see cref="IValueSourceAsync"/> instance for <see cref="ValueOriginKind.ValueSource"/>,
    /// otherwise <c>null</c>.
    /// </summary>
    public object? Source { get; }

    /// <summary>
    /// The name of the value source: <see cref="INamedValueSource.Name"/> when the source implements it, otherwise
    /// its type name. <c>null</c> when the value does not come from a value source.
    /// </summary>
    public string? SourceName { get; }

    /// <summary>The layer the value source was registered for; <c>null</c> when the value does not come from a value source.</summary>
    public ValueSourceLayer? Layer { get; }

    /// <summary>
    /// <c>true</c> when the value is an inherited value: one the user did not choose, which is therefore not
    /// written to the user file (compiled default, defaults file, value source in the <see cref="ValueSourceLayer.Default"/> layer).
    /// </summary>
    internal bool IsInherited => Kind is ValueOriginKind.CompiledDefault or ValueOriginKind.DefaultsFile
                                 || (Kind == ValueOriginKind.ValueSource && Layer == ValueSourceLayer.Default);

    /// <summary>
    /// <c>true</c> when the value comes from a layer above the user file (constants file, value source in the
    /// <see cref="ValueSourceLayer.Override"/> or <see cref="ValueSourceLayer.Constant"/> layer), which hides the
    /// user's own value for as long as it supplies one.
    /// </summary>
    internal bool IsAboveUser => Kind == ValueOriginKind.ConstantsFile
                                 || (Kind == ValueOriginKind.ValueSource && Layer != ValueSourceLayer.Default);

    /// <inheritdoc/>
    public bool Equals(ValueOrigin? other)
        => other is not null && Kind == other.Kind && Layer == other.Layer && ReferenceEquals(Source, other.Source)
           && string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ValueOrigin);

    /// <inheritdoc/>
    public override int GetHashCode() => ((int)Kind * 397) ^ (Layer.GetHashCode() * 31) ^ (Source?.GetHashCode() ?? 0);

    /// <summary>For example <c>DefaultsFile "C:\app\defaults.ini"</c> or <c>ValueSource "Windows policy (HKLM, mandatory)" (Constant)</c>.</summary>
    public override string ToString()
    {
        if (Kind == ValueOriginKind.ValueSource)
            return $"{Kind} \"{SourceName}\" ({Layer})";
        return FilePath != null ? $"{Kind} \"{FilePath}\"" : Kind.ToString();
    }
}
