// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Internationalization.Interfaces;

/// <summary>
/// Optional marker interface for language sections generated from interfaces annotated with
/// <see cref="Attributes.IniLanguageSectionAttribute"/>.
/// Consumer interfaces are not required to extend this.
/// </summary>
public interface ILanguageSection
{
    /// <summary>
    /// Optional section name used for file naming and section routing.
    /// Corresponds to <see cref="Attributes.IniLanguageSectionAttribute.SectionName"/>.
    /// <c>null</c> when no section name was specified.
    /// </summary>
    string? SectionName { get; }
}


