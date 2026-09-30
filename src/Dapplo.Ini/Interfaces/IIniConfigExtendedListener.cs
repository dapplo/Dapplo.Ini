// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Interfaces;

/// <summary>
/// Optional extension of <see cref="IIniConfigListener"/> with additional notifications.
/// </summary>
/// <remarks>
/// A listener registered via <see cref="IniConfigBuilder.AddListener"/> that also implements this interface
/// receives these callbacks too. It is a separate interface so that existing <see cref="IIniConfigListener"/>
/// implementations keep compiling (default interface members are not available on .NET Framework).
/// Derive from <see cref="IniConfigListenerBase"/> to implement only the callbacks you need.
/// </remarks>
public interface IIniConfigExtendedListener
{
    /// <summary>
    /// Called after a section has been registered via <see cref="IniConfig.AddSection{T}"/> /
    /// <see cref="IniConfig.AddSectionAsync{T}"/> (not for sections registered on the builder).
    /// </summary>
    /// <param name="sectionName">The INI section name of the registered section.</param>
    /// <param name="loaded">
    /// <c>true</c> when the configuration was already loaded and the section was populated right away
    /// from the retained file data (see <see cref="IniConfigBuilder.AllowLateSectionRegistration"/>);
    /// <c>false</c> when the section was registered before the load and will be populated by it.
    /// </param>
    void OnSectionAdded(string sectionName, bool loaded);

    /// <summary>
    /// Called when an external value source (<see cref="IValueSource"/> / <see cref="IValueSourceAsync"/>)
    /// supplies a value for a key that is set by a constants file. Constants win: the value is not applied.
    /// </summary>
    /// <param name="sectionName">The INI section name.</param>
    /// <param name="key">The key protected by the constants file.</param>
    /// <param name="ignoredValue">The value the source supplied, which was not applied.</param>
    void OnValueSourceIgnored(string sectionName, string key, string? ignoredValue);
}

/// <summary>
/// Convenience base class for listeners: implements <see cref="IIniConfigListener"/> and
/// <see cref="IIniConfigExtendedListener"/> with empty virtual methods, so a derived class only
/// overrides the callbacks it is interested in.
/// </summary>
public abstract class IniConfigListenerBase : IIniConfigListener, IIniConfigExtendedListener
{
    /// <inheritdoc/>
    public virtual void OnFileLoaded(string filePath) { }

    /// <inheritdoc/>
    public virtual void OnFileNotFound(string fileName) { }

    /// <inheritdoc/>
    public virtual void OnSaved(string filePath) { }

    /// <inheritdoc/>
    public virtual void OnReloaded(string filePath) { }

    /// <inheritdoc/>
    public virtual void OnError(string operation, Exception exception) { }

    /// <inheritdoc/>
    public virtual void OnUnknownKey(string sectionName, string key, string? rawValue) { }

    /// <inheritdoc/>
    public virtual void OnValueConversionFailed(string sectionName, string key, string? rawValue, Exception exception) { }

    /// <inheritdoc/>
    public virtual void OnSectionAdded(string sectionName, bool loaded) { }

    /// <inheritdoc/>
    public virtual void OnValueSourceIgnored(string sectionName, string key, string? ignoredValue) { }
}
