// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;

namespace Dapplo.Ini.Internationalization;

/// <summary>
/// An available language, returned by <see cref="Configuration.LanguageConfig.GetLanguages"/>.
/// </summary>
public sealed class LanguageInfo
{
    /// <summary>Creates a language description.</summary>
    /// <param name="ietf">The IETF tag from the file name, e.g. <c>de-DE</c>.</param>
    /// <param name="nativeName">The <see cref="CultureInfo.NativeName"/>, or <c>null</c> when the system does not know the tag.</param>
    /// <param name="description">The <c>Description</c> from the <c>[__language__]</c> section of the file, or <c>null</c>.</param>
    /// <param name="hasBaseFile">Whether a base file (<c>{basename}.{ietf}.ini</c>) exists for the language.</param>
    public LanguageInfo(string ietf, string? nativeName, string? description, bool hasBaseFile)
    {
        Ietf = ietf ?? throw new ArgumentNullException(nameof(ietf));
        NativeName = nativeName;
        Description = description;
        HasBaseFile = hasBaseFile;
    }

    /// <summary>The IETF tag as spelled in the file name, e.g. <c>de-DE</c> or <c>de-x-franconia</c>.</summary>
    public string Ietf { get; }

    /// <summary>
    /// The <see cref="CultureInfo.NativeName"/> of the tag, or <c>null</c> when the system does not know the tag
    /// (e.g. <c>de-x-franconia</c>, or <c>fr-QC</c> on older systems).
    /// </summary>
    public string? NativeName { get; }

    /// <summary>
    /// The description from the language file:
    /// <code>
    /// [__language__]
    /// Description=Fränkisch
    /// </code>
    /// or <c>null</c> when the file has none.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// <c>true</c> when a base file (<c>{basename}.{ietf}.ini</c>) exists for this language; <c>false</c> when only
    /// module files define it (only when every registered section is a module section).
    /// </summary>
    public bool HasBaseFile { get; }

    /// <summary>The name to show in a language picker: <see cref="Description"/>, else <see cref="NativeName"/>, else <see cref="Ietf"/>.</summary>
    public string DisplayName => Description ?? NativeName ?? Ietf;

    /// <inheritdoc/>
    public override string ToString() => $"{Ietf} ({DisplayName})";
}
