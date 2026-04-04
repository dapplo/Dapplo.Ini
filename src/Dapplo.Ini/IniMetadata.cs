// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini;

/// <summary>
/// Metadata read from the <c>[__metadata__]</c> section of the INI file on the last load.
/// Access via <see cref="Configuration.IniConfig.Metadata"/> inside an
/// <see cref="Interfaces.IAfterLoad"/> hook.
/// See the Migration wiki page for full examples.
/// </summary>
public sealed class IniMetadata
{
    /// <summary>
    /// Semantic version string from the <c>Version</c> key; <c>null</c> when absent.
    /// When written from <see cref="Configuration.IniConfigBuilder.EnableMetadata"/>,
    /// this is the portion of <c>AssemblyInformationalVersion</c> before any <c>+</c>
    /// (i.e. the SemVer part), or the plain <c>AssemblyVersion</c> as a fallback.
    /// </summary>
    public string? Version { get; internal set; }

    /// <summary>Application name from the <c>CreatedBy</c> key; <c>null</c> when absent.</summary>
    public string? ApplicationName { get; internal set; }

    /// <summary>
    /// Locale-formatted save timestamp from the <c>SavedOn</c> key; <c>null</c> when absent.
    /// Intended for human inspection only — do not parse programmatically.
    /// </summary>
    public string? SavedOn { get; internal set; }

    /// <summary>
    /// Source-control commit hash from the <c>CommitHash</c> key; <c>null</c> when absent.
    /// When written from <see cref="Configuration.IniConfigBuilder.EnableMetadata"/>,
    /// this is the portion of <c>AssemblyInformationalVersion</c> after the <c>+</c>
    /// separator (e.g. <c>"abc1234def5678"</c> from <c>"1.2.0+abc1234def5678"</c>).
    /// </summary>
    public string? CommitHash { get; internal set; }
}

/// <summary>
/// Internal transfer object carrying the consumer-configured values for <c>[__metadata__]</c>
/// from <see cref="Configuration.IniConfigBuilder"/> to <see cref="Configuration.IniConfig"/>.
/// </summary>
internal sealed class IniMetadataConfig
{
    public string? Version { get; set; }
    public string? ApplicationName { get; set; }
    public string? CommitHash { get; set; }
}
