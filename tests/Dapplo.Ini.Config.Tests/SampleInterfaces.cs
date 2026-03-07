// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Config.Attributes;
using Dapplo.Ini.Config.Interfaces;

namespace Dapplo.Ini.Config.Tests;

// ── Sample interfaces used by all tests ───────────────────────────────────────

/// <summary>Basic section with common value types.</summary>
[IniSection("General", Description = "General application settings")]
public interface IGeneralSettings : IIniSection
{
    [IniValue(DefaultValue = "MyApp", Description = "Application name", NotifyPropertyChanged = true)]
    string? AppName { get; set; }

    [IniValue(DefaultValue = "42")]
    int MaxRetries { get; set; }

    [IniValue(DefaultValue = "True")]
    bool EnableLogging { get; set; }

    [IniValue(DefaultValue = "3.14")]
    double Threshold { get; set; }
}

/// <summary>Section with transactional properties.</summary>
[IniSection]
public interface IUserSettings : IIniSection, ITransactional
{
    [IniValue(DefaultValue = "admin", Transactional = true)]
    string? Username { get; set; }

    [IniValue(DefaultValue = "password", Transactional = true)]
    string? Password { get; set; }

    [IniValue(DefaultValue = "0")]
    int LoginCount { get; set; }
}

/// <summary>
/// Section that hooks into save/load lifecycle.
/// The consumer implements the hook methods in a partial class file.
/// </summary>
[IniSection]
public interface ILifecycleSettings : IIniSection, IAfterLoad, IBeforeSave, IAfterSave
{
    string? Value { get; set; }
}
