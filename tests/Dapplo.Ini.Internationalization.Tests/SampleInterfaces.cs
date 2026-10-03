// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Internationalization.Attributes;
using Dapplo.Ini.Internationalization.Interfaces;

namespace Dapplo.Ini.Internationalization.Tests;

/// <summary>
/// Base language section.
/// No explicit section name → SectionName derived from interface = "MainLanguage".
/// No ModuleName → file is <c>testapp.{ietf}.ini</c>, reads <c>[MainLanguage]</c> block.
/// Does NOT extend ILanguageSection to verify that is optional.
/// </summary>
[IniLanguageSection]
public interface IMainLanguage
{
    string WelcomeMessage { get; }
    string ErrorTitle { get; }
    string SaveButton { get; }
    string CancelButton { get; }
    string MultiLine { get; }
    string TabValue { get; }
    string BackslashValue { get; }
}

/// <summary>
/// Named section — no explicit name, derives to "CoreLanguage" from interface name.
/// No ModuleName → reads the <c>[CoreLanguage]</c> block from <c>testapp.{ietf}.ini</c>.
/// Optionally extends ILanguageSection to show it still works when present.
/// </summary>
[IniLanguageSection]
public interface ICoreLanguage : ILanguageSection
{
    string CoreTitle { get; }
    string CoreStatus { get; }
}

/// <summary>
/// Language section whose interface also extends IReadOnlyDictionary so the dynamic
/// indexer can be used.
/// SectionName derived = "DictionaryLanguage", reads <c>[DictionaryLanguage]</c> from main file.
/// </summary>
[IniLanguageSection]
public interface IDictionaryLanguage : IReadOnlyDictionary<string, string>
{
    string WelcomeMessage { get; }
}

/// <summary>
/// Plugin / module language section.
/// No explicit SectionName → derives to "PluginLanguage" from interface name.
/// ModuleName = "core" → reads <c>[PluginLanguage]</c> from <c>testapp.core.{ietf}.ini</c>.
/// Demonstrates separate file selection (<see cref="IniLanguageSectionAttribute.ModuleName"/>)
/// and section routing (<see cref="IniLanguageSectionAttribute.SectionName"/>).
/// </summary>
[IniLanguageSection(ModuleName = "core")]
public interface IPluginLanguage
{
    string PluginTitle { get; }
    string PluginStatus { get; }
}

/// <summary>
/// Language section extending INotifyPropertyChanged to test UI data-binding updates.
/// </summary>
[IniLanguageSection("MainLanguage")]
public interface INotifyMainLanguage : System.ComponentModel.INotifyPropertyChanged
{
    string WelcomeMessage { get; }
    string ErrorTitle { get; }
    string SaveButton { get; }
    string CancelButton { get; }
    string MultiLine { get; }
    string TabValue { get; }
    string BackslashValue { get; }
}

/// <summary>
/// Language section extending both INotifyPropertyChanged and INotifyPropertyChanging,
/// with per-property suppression attributes.
/// </summary>
[IniLanguageSection("MainLanguage")]
public interface INpcBothLanguage : System.ComponentModel.INotifyPropertyChanged, System.ComponentModel.INotifyPropertyChanging
{
    string WelcomeMessage { get; }

    [Dapplo.Ini.Attributes.IniValue(SuppressPropertyChanged = true)]
    string ErrorTitle { get; }

    [Dapplo.Ini.Attributes.IniValue(SuppressPropertyChanging = true)]
    string SaveButton { get; }
}

/// <summary>
/// A plugin section like Greenshot's Imgur plugin: module file <c>{basename}.imgur.{ietf}.ini</c>, section <c>[Imgur]</c>,
/// live language switch via INotifyPropertyChanged.
/// </summary>
[IniLanguageSection("Imgur", ModuleName = "imgur")]
public interface IImgurLanguage : System.ComponentModel.INotifyPropertyChanged
{
    string History { get; }
    string Upload { get; }
}

/// <summary>A plugin section with a three letter module name, which could be mistaken for a language tag.</summary>
[IniLanguageSection("Box", ModuleName = "box")]
public interface IBoxLanguage
{
    string Upload { get; }
}

/// <summary>A second host section in the base file.</summary>
[IniLanguageSection("Editor")]
public interface IEditorLanguage
{
    string Title { get; }
    string Undo { get; }
}

/// <summary>Uses the reserved section name, which must be rejected on registration.</summary>
[IniLanguageSection("__language__")]
public interface IReservedLanguage
{
    string Description { get; }
}
