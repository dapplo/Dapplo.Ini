// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.WindowsPolicies.Tests;

public enum OutputFormat
{
    Png,
    Jpg,
    [Description("Bitmap (uncompressed)")]
    Bmp
}

[IniSection("Core", Description = "Core settings")]
public interface ICoreSettings : IIniSection
{
    [IniValue(DefaultValue = "True", Description = "Check for updates on start")]
    bool CheckForUpdates { get; set; }

    [IniValue(DefaultValue = "5", Description = "How often to retry")]
    [Range(1, 10)]
    int MaxRetries { get; set; }

    [IniValue(DefaultValue = "0")]
    uint Flags { get; set; }

    [IniValue(DefaultValue = "0")]
    long BigNumber { get; set; }

    [IniValue(DefaultValue = "0")]
    [Range(-5, 5)]
    int Offset { get; set; }

    [IniValue(DefaultValue = "Png", Description = "The output format")]
    OutputFormat OutputFileFormat { get; set; }

    [IniValue(DefaultValue = "MyApp")]
    string? AppName { get; set; }

    [IniValue(DefaultValue = @"%TEMP%\out")]
    [PolicyExpandable]
    string? OutputPath { get; set; }

    [IniValue(DefaultValue = "1.5")]
    double Threshold { get; set; }

    [IniValue(DefaultValue = "a,b")]
    List<string>? Plugins { get; set; }

    [IniValue(DefaultValue = "1,2")]
    List<int>? Ports { get; set; }

    [IniValue(DefaultValue = "x=1")]
    Dictionary<string, string>? Headers { get; set; }

    [IniValue(DefaultValue = "")]
    [Secret]
    string? ApiToken { get; set; }

    [IniValue(DefaultValue = "")]
    [NotAdministrable]
    string? LastUsedFolder { get; set; }

    [IniValue(DefaultValue = "0", RuntimeOnly = true)]
    int SessionCount { get; set; }

    [IniValue(DefaultValue = "user", IgnoreConstants = true)]
    string? Nickname { get; set; }

    [IniValue(DefaultValue = "Common")]
    [PolicyCategory("Common")]
    [PolicyDisplayName("Language of the user interface")]
    string? Language { get; set; }
}

[IniSection("Hidden")]
[NotAdministrable]
public interface IHiddenSettings : IIniSection
{
    [IniValue(DefaultValue = "x")]
    string? Value { get; set; }
}
