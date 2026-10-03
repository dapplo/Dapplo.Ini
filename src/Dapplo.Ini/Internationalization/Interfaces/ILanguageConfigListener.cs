// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Internationalization.Interfaces;

/// <summary>
/// Optional listener interface with notifications that only a <see cref="Configuration.LanguageConfig"/> raises.
/// </summary>
/// <remarks>
/// A listener registered via <see cref="Configuration.LanguageConfigBuilder.AddListener"/> (which takes an
/// <see cref="IIniConfigListener"/>) that also implements this interface receives these callbacks too.
/// Derive from <see cref="LanguageConfigListenerBase"/> to implement only the callbacks you need.
/// </remarks>
public interface ILanguageConfigListener
{
    /// <summary>
    /// Called with <see cref="Configuration.LanguageConfigBuilder.ResolveLanguages"/> each time a requested language
    /// is resolved (load and language switch), also when the resolved tag equals the requested one.
    /// </summary>
    /// <param name="requested">The requested IETF tag.</param>
    /// <param name="resolved">The tag that is used (see <see cref="Configuration.LanguageConfig.ResolveLanguage"/>).</param>
    void OnLanguageResolved(string requested, string resolved);

    /// <summary>
    /// Called when <see cref="Configuration.LanguageConfig.GetTranslation"/> or
    /// <see cref="Configuration.LanguageSectionBase.Format"/> does not find a key and returns the <c>###key###</c> sentinel.
    /// </summary>
    /// <param name="moduleOrSection">The section name for <c>Format</c>; <c>null</c> for a lookup without section.</param>
    /// <param name="key">The requested key.</param>
    void OnTranslationNotFound(string? moduleOrSection, string key);

    /// <summary>
    /// Called when <see cref="Configuration.LanguageSectionBase.Format"/> cannot format a translation (e.g. a bad
    /// format string or too few arguments). <c>Format</c> then returns the unformatted translation.
    /// </summary>
    /// <param name="sectionName">The section name.</param>
    /// <param name="key">The requested key.</param>
    /// <param name="exception">The exception thrown by <see cref="string.Format(string, object[])"/>.</param>
    void OnFormatFailed(string sectionName, string key, Exception exception);
}

/// <summary>
/// Convenience base class for language listeners: implements <see cref="IIniConfigListener"/>,
/// <see cref="IIniConfigExtendedListener"/> and <see cref="ILanguageConfigListener"/> with empty virtual methods.
/// </summary>
public abstract class LanguageConfigListenerBase : IniConfigListenerBase, ILanguageConfigListener
{
    /// <inheritdoc/>
    public virtual void OnLanguageResolved(string requested, string resolved) { }

    /// <inheritdoc/>
    public virtual void OnTranslationNotFound(string? moduleOrSection, string key) { }

    /// <inheritdoc/>
    public virtual void OnFormatFailed(string sectionName, string key, Exception exception) { }
}
