// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Interfaces;

/// <summary>
/// Listener interface for receiving diagnostic events from the Dapplo.Ini framework.
/// Implement this interface in your own class, then register the instance via
/// <see cref="Configuration.IniConfigBuilder.AddListener"/> (INI configuration) or
/// <see cref="Internationalization.Configuration.LanguageConfigBuilder.AddListener"/>
/// (language/i18n configuration).
/// </summary>
/// <remarks>
/// <para>
/// There is zero overhead when no listener is registered, and minimal overhead when one or
/// more are registered (a single <c>Count == 0</c> guard is all that is evaluated on each
/// operation when the listener list is empty).
/// No logging framework dependency is introduced by this interface.
/// </para>
/// <para>
/// See the project wiki page <em>Listeners</em> for the full callback reference, a complete
/// logging example, and notes on which callbacks are raised by <c>LanguageConfig</c>.
/// </para>
/// </remarks>
public interface IIniConfigListener
{
    /// <summary>
    /// Called after a configuration file has been successfully found and its values applied to sections.
    /// </summary>
    /// <param name="filePath">The absolute path of the file that was loaded.</param>
    void OnFileLoaded(string filePath);

    /// <summary>
    /// Called when a configuration file could not be found at any of the configured search paths.
    /// All sections fall back to their compiled default values.
    /// </summary>
    /// <param name="fileName">The file name that was searched for (e.g. <c>"app.ini"</c>).</param>
    void OnFileNotFound(string fileName);

    /// <summary>
    /// Called after the configuration has been successfully written to disk.
    /// Not called by <see cref="Internationalization.Configuration.LanguageConfig"/> as language
    /// files are read-only.
    /// </summary>
    /// <param name="filePath">The absolute path of the file that was written.</param>
    void OnSaved(string filePath);

    /// <summary>
    /// Called after the configuration has been successfully reloaded from disk
    /// (triggered by <see cref="Configuration.IniConfig.Reload"/>, an external file-change,
    /// or a language switch in <see cref="Internationalization.Configuration.LanguageConfig"/>).
    /// </summary>
    /// <param name="filePath">The absolute path of the file that was reloaded.</param>
    void OnReloaded(string filePath);

    /// <summary>
    /// Called when an exception is thrown during a load, reload, or save operation.
    /// The exception is always re-thrown after all listeners have been notified.
    /// </summary>
    /// <param name="operation">
    /// A short description of the operation that failed (e.g. <c>"Load"</c>, <c>"Save"</c>,
    /// <c>"Reload"</c>).
    /// </param>
    /// <param name="exception">The exception that was thrown.</param>
    void OnError(string operation, Exception exception);

    /// <summary>
    /// Called when a key read from an INI file has no matching property on the registered section
    /// interface.
    /// </summary>
    /// <param name="sectionName">The INI section name where the key was found.</param>
    /// <param name="key">The unrecognised key name.</param>
    /// <param name="rawValue">The raw string value associated with the key.</param>
    void OnUnknownKey(string sectionName, string key, string? rawValue);

    /// <summary>
    /// Called when a raw INI string value cannot be converted to the target property type.
    /// The property retains its default value.
    /// </summary>
    /// <param name="sectionName">The INI section name containing the property.</param>
    /// <param name="key">The property key whose value could not be converted.</param>
    /// <param name="rawValue">The raw string that failed to convert.</param>
    /// <param name="exception">The exception thrown by the converter.</param>
    void OnValueConversionFailed(string sectionName, string key, string? rawValue, Exception exception);
}
