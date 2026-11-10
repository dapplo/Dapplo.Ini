// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Configuration;

/// <summary>Where the current value of a key came from.</summary>
internal enum ValueOriginKind
{
    /// <summary>The compiled default (<c>[DefaultValue]</c> / <c>[IniValue(DefaultValue = …)]</c>) of the property.</summary>
    CompiledDefault,
    /// <summary>A defaults file registered with <see cref="IniConfigBuilder.AddDefaultsFile"/>.</summary>
    DefaultsFile,
    /// <summary>The user file, or a value the application set after the load.</summary>
    UserFile,
    /// <summary>A constants file registered with <see cref="IniConfigBuilder.AddConstantsFile"/>.</summary>
    ConstantsFile,
    /// <summary>An external value source registered with <see cref="IniConfigBuilder.AddValueSource(Interfaces.IValueSource)"/>.</summary>
    ValueSource
}

/// <summary>
/// Describes where the current value of a key came from.
/// </summary>
internal sealed class ValueOrigin
{
    /// <summary>The value is the compiled default of the property.</summary>
    public static readonly ValueOrigin CompiledDefault = new(ValueOriginKind.CompiledDefault, null, null);

    /// <summary>The value was set by the application after the load (it is written to the user file on save).</summary>
    public static readonly ValueOrigin Application = new(ValueOriginKind.UserFile, null, null);

    internal ValueOrigin(ValueOriginKind kind, string? filePath, object? source)
    {
        Kind = kind;
        FilePath = filePath;
        Source = source;
    }

    /// <summary>The kind of layer that supplied the value.</summary>
    public ValueOriginKind Kind { get; }

    /// <summary>
    /// The path of the file that supplied the value, for <see cref="ValueOriginKind.DefaultsFile"/>,
    /// <see cref="ValueOriginKind.UserFile"/> and <see cref="ValueOriginKind.ConstantsFile"/>.
    /// <c>null</c> for a value the application set after the load.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>The value source instance for <see cref="ValueOriginKind.ValueSource"/>, otherwise <c>null</c>.</summary>
    public object? Source { get; }

    /// <summary>
    /// <c>true</c> when the value is an inherited value: one the user did not choose, which is therefore not
    /// written to the user file (compiled default, defaults file).
    /// </summary>
    internal bool IsInherited => Kind is ValueOriginKind.CompiledDefault or ValueOriginKind.DefaultsFile;

    /// <summary>
    /// <c>true</c> when the value comes from a layer above the user file (constants file, value source), which
    /// hides the user's own value for as long as it supplies one.
    /// </summary>
    internal bool IsAboveUser => Kind is ValueOriginKind.ConstantsFile or ValueOriginKind.ValueSource;

    /// <inheritdoc/>
    public override string ToString() => FilePath != null ? $"{Kind} \"{FilePath}\"" : Kind.ToString();
}
