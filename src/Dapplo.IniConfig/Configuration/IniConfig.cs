// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.IniConfig.Interfaces;
using Dapplo.IniConfig.Parsing;

namespace Dapplo.IniConfig.Configuration;

/// <summary>
/// Holds configuration for one registered INI file: its search locations, defaults/constants files,
/// and the <see cref="IIniSection"/> instances that were loaded from it.
/// </summary>
public sealed class IniConfig
{
    internal readonly List<string> SearchPaths = new();
    internal readonly List<string> DefaultFilePaths = new();
    internal readonly List<string> ConstantFilePaths = new();
    internal readonly Dictionary<Type, IIniSection> Sections = new();

    /// <summary>The logical name of the INI file (e.g. "myapp.ini").</summary>
    public string FileName { get; }

    /// <summary>The resolved absolute path from which the file was loaded, or <c>null</c> if not yet loaded.</summary>
    public string? LoadedFromPath { get; internal set; }

    internal IniConfig(string fileName)
    {
        FileName = fileName;
    }

    /// <summary>
    /// Returns the registered section of type <typeparamref name="T"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the type has not been registered.</exception>
    public T GetSection<T>() where T : IIniSection
    {
        if (Sections.TryGetValue(typeof(T), out var section))
            return (T)section;

        throw new InvalidOperationException(
            $"Section '{typeof(T).Name}' has not been registered with the INI configuration '{FileName}'.");
    }

    /// <summary>Saves all sections back to <see cref="LoadedFromPath"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the file path is not known.</exception>
    public void Save()
    {
        if (string.IsNullOrEmpty(LoadedFromPath))
            throw new InvalidOperationException("Cannot save: the INI file path is not known.");

        // Call IBeforeSave hooks; abort if any returns false
        foreach (var section in Sections.Values)
        {
            if (section is IBeforeSave beforeSave && !beforeSave.OnBeforeSave())
                return;
        }

        // Build an IniFile from current section values
        var iniFile = BuildIniFile();
        IniFileWriter.WriteFile(LoadedFromPath!, iniFile);

        // Call IAfterSave hooks
        foreach (var section in Sections.Values)
        {
            if (section is IAfterSave afterSave)
                afterSave.OnAfterSave();
        }
    }

    internal IniFile BuildIniFile()
    {
        var iniFile = new Parsing.IniFile();
        foreach (var section in Sections.Values)
        {
            var iniSection = iniFile.GetOrAddSection(section.SectionName);
            // Ask the section to populate keys via its GetRawValue / SetRawValue contract
            // The generated class exposes a GetAllRawValues() that we call via the base class
            if (section is IniSectionBase sectionBase)
            {
                foreach (var kvp in sectionBase.GetAllRawValues())
                    iniSection.SetValue(kvp.Key, kvp.Value);
            }
        }
        return iniFile;
    }
}
