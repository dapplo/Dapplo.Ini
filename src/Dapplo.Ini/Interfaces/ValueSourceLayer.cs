// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Interfaces;

/// <summary>
/// The layer a value source is applied in, see
/// <see cref="Configuration.IniConfigBuilder.AddValueSource(IValueSource, ValueSourceLayer)"/>.
/// </summary>
/// <remarks>
/// The layers of a load, lowest first (a later one wins): compiled defaults, defaults files,
/// <see cref="Default"/> sources, user file, constants files, <see cref="Constant"/> sources,
/// <see cref="Override"/> sources (skipped for constant keys). Inside a layer the sources are applied in
/// registration order, synchronous sources before asynchronous ones; a later source wins.
/// </remarks>
public enum ValueSourceLayer
{
    /// <summary>
    /// Applied after the defaults files, before the user file: a default the user can change. Its values are
    /// inherited values, not written to the user file, so a later change of the source reaches every key the
    /// user didn't set. Respects <c>IgnoreDefaults</c> on section and key.
    /// </summary>
    Default,

    /// <summary>
    /// Applied after the constants files, over the user's value, but skipped for constant keys
    /// (reported via <see cref="IIniConfigExtendedListener.OnValueSourceIgnored"/>). Not written to the user file;
    /// the user's own value below it stays there. This is the layer of the overloads without a layer.
    /// </summary>
    Override,

    /// <summary>
    /// Applied after the constants files and wins over them: marks the key constant exactly like a constants file
    /// (<see cref="IIniSection.IsConstant"/> is <c>true</c>, setters throw), respecting <c>IgnoreConstants</c> on
    /// section and key, and is never written to the user file. When it replaces a value of a constants file,
    /// <see cref="IIniConfigExtendedListener.OnValueSourceIgnored"/> reports the constants-file value that lost.
    /// </summary>
    Constant
}

/// <summary>
/// Optionally implemented by an <see cref="IValueSource"/> or <see cref="IValueSourceAsync"/> to give it a name
/// for diagnostics, e.g. in <see cref="Configuration.ValueOrigin.SourceName"/>.
/// </summary>
public interface INamedValueSource
{
    /// <summary>A readable name, e.g. <c>Windows policy (HKLM, mandatory)</c>.</summary>
    string Name { get; }
}
