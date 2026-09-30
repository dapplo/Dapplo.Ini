// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Interfaces;

/// <summary>
/// Optional extension of <see cref="IIniConfigListener"/> for section registrations.
/// </summary>
/// <remarks>
/// A listener registered via <see cref="IniConfigBuilder.AddListener"/> that also implements this
/// interface is notified when <see cref="IniConfig.AddSection{T}"/> / <see cref="IniConfig.AddSectionAsync{T}"/>
/// registers a section. It is a separate interface so that existing <see cref="IIniConfigListener"/>
/// implementations keep compiling (default interface members are not available on .NET Framework).
/// </remarks>
public interface IIniConfigSectionListener
{
    /// <summary>
    /// Called after a section has been registered with the configuration.
    /// </summary>
    /// <param name="sectionName">The INI section name of the registered section.</param>
    /// <param name="loaded">
    /// <c>true</c> when the configuration was already loaded and the section was populated right away
    /// from the retained file data (see <see cref="IniConfigBuilder.AllowLateSectionRegistration"/>);
    /// <c>false</c> when the section was registered before the load and will be populated by it.
    /// </param>
    void OnSectionAdded(string sectionName, bool loaded);
}
