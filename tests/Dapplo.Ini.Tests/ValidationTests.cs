// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using Dapplo.Ini;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

/// <summary>
/// Tests for the <see cref="IDataValidation{TSelf}"/> interface and
/// <see cref="System.ComponentModel.INotifyDataErrorInfo"/> integration.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class ValidationTests : IDisposable
{
    private readonly string _tempDir;

    public ValidationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        IniConfigRegistry.Clear();
    }

    public void Dispose()
    {
        IniConfigRegistry.Clear();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ServerConfigSettingsImpl BuildSection()
    {
        var section = new ServerConfigSettingsImpl();
        IniConfigRegistry.ForFile("validate.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IServerConfigSettings>(section)
            .Build();
        return section;
    }

    private string WriteIni(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GeneratedClass_ImplementsINotifyDataErrorInfo()
    {
        var section = new ServerConfigSettingsImpl();
        Assert.IsAssignableFrom<INotifyDataErrorInfo>(section);
    }

    [Fact]
    public void ValidProperty_HasNoErrors()
    {
        var section = BuildSection();
        // Default port = 8080, which is valid
        var dataErrorInfo = (INotifyDataErrorInfo)section;
        Assert.False(dataErrorInfo.HasErrors);
        Assert.Empty(dataErrorInfo.GetErrors(nameof(IServerConfigSettings.Port)).Cast<string>());
    }

    [Fact]
    public void InvalidPort_HasErrors()
    {
        var section = BuildSection();
        var dataErrorInfo = (INotifyDataErrorInfo)section;

        section.Port = 0; // invalid
        Assert.True(dataErrorInfo.HasErrors);
        var errors = dataErrorInfo.GetErrors(nameof(IServerConfigSettings.Port)).Cast<string>().ToList();
        Assert.NotEmpty(errors);
        Assert.Contains("Port must be between 1 and 65535.", errors);
    }

    [Fact]
    public void FixingInvalidPort_ClearsErrors()
    {
        var section = BuildSection();
        var dataErrorInfo = (INotifyDataErrorInfo)section;

        section.Port = 0;       // invalid
        Assert.True(dataErrorInfo.HasErrors);

        section.Port = 443;     // valid
        Assert.False(dataErrorInfo.HasErrors);
    }

    [Fact]
    public void ErrorsChangedEvent_IsRaisedWhenPortChanges()
    {
        var section = BuildSection();
        var changedProps = new List<string?>();
        ((INotifyDataErrorInfo)section).ErrorsChanged += (_, e) => changedProps.Add(e.PropertyName);

        section.Port = -1; // triggers validation
        Assert.Contains(nameof(IServerConfigSettings.Port), changedProps);
    }

    [Fact]
    public void EmptyHost_HasErrors()
    {
        var section = BuildSection();
        var dataErrorInfo = (INotifyDataErrorInfo)section;

        section.Host = "  "; // whitespace only
        Assert.True(dataErrorInfo.HasErrors);
        var errors = dataErrorInfo.GetErrors(nameof(IServerConfigSettings.Host)).Cast<string>().ToList();
        Assert.Contains("Host must not be empty.", errors);
    }

    [Fact]
    public void GetErrors_WithNullPropertyName_ReturnsAllErrors()
    {
        var section = BuildSection();
        var dataErrorInfo = (INotifyDataErrorInfo)section;

        section.Port = 0;
        section.Host = "";

        var allErrors = dataErrorInfo.GetErrors(null).Cast<string>().ToList();
        Assert.True(allErrors.Count >= 2);
    }

    [Fact]
    public void IDataValidation_NonGeneric_Bridge_Works()
    {
        var section = BuildSection();
        var validation = (IDataValidation)section;

        section.Port = 99999;
        var errors = validation.ValidateProperty(nameof(IServerConfigSettings.Port)).ToList();
        Assert.NotEmpty(errors);
    }

    // ── Post-load validation (IDataValidation<TSelf>) ─────────────────────────

    /// <summary>
    /// When an INI file contains an invalid value and the section uses
    /// <see cref="IDataValidation{TSelf}"/>, the generated IAfterLoad bridge
    /// must run <c>RunAllValidations()</c> so that errors are immediately
    /// available after Build() — without any property-setter interaction.
    /// </summary>
    [Fact]
    public void IDataValidation_ValidationRunsAfterLoad_WhenFileContainsInvalidPort()
    {
        // Write a file with Port = 0 (invalid: must be 1–65535)
        WriteIni("dataval_load.ini", "[ServerConfig]\nPort = 0\nHost = myhost");

        var section = new ServerConfigSettingsImpl();
        IniConfigRegistry.ForFile("dataval_load.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IServerConfigSettings>(section)
            .Build();

        // Errors should be populated immediately after Build() — no setter call needed
        var errors = ((INotifyDataErrorInfo)section)
            .GetErrors(nameof(IServerConfigSettings.Port)).Cast<string>().ToList();
        Assert.NotEmpty(errors);
        Assert.Contains("Port must be between 1 and 65535.", errors);
    }

    [Fact]
    public void IDataValidation_ValidationRunsAfterLoad_ValidValuesHaveNoErrors()
    {
        WriteIni("dataval_valid.ini", "[ServerConfig]\nPort = 443\nHost = myhost");

        var section = new ServerConfigSettingsImpl();
        IniConfigRegistry.ForFile("dataval_valid.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IServerConfigSettings>(section)
            .Build();

        Assert.False(((INotifyDataErrorInfo)section).HasErrors);
    }

    // ── RunAllValidations ─────────────────────────────────────────────────────

    /// <summary>
    /// <c>RunAllValidations()</c> re-fires <c>ErrorsChanged</c> for every non-ignored
    /// property.  This is the recommended pattern for settings screens: call it after
    /// the UI window is shown so that WPF/Avalonia bindings pick up any pre-existing
    /// validation errors.
    /// </summary>
    [Fact]
    public void RunAllValidations_FiresErrorsChangedForAllProperties()
    {
        var section = BuildSection();
        var changedProps = new List<string?>();
        ((INotifyDataErrorInfo)section).ErrorsChanged += (_, e) => changedProps.Add(e.PropertyName);

        section.RunAllValidations();

        // Both Port and Host should have fired ErrorsChanged
        Assert.Contains(nameof(IServerConfigSettings.Port), changedProps);
        Assert.Contains(nameof(IServerConfigSettings.Host), changedProps);
    }

    [Fact]
    public void RunAllValidations_ReportsInvalidValuesSet_BeforeWindowOpens()
    {
        // Simulate: settings loaded with invalid values, window then opens and
        // calls RunAllValidations() to surface the errors
        WriteIni("screen.ini", "[ServerConfig]\nPort = 0\nHost = myhost");

        var section = new ServerConfigSettingsImpl();
        IniConfigRegistry.ForFile("screen.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IServerConfigSettings>(section)
            .Build();

        // Simulate "window opened" — re-raise all error events for WPF bindings
        var changedProps = new List<string?>();
        ((INotifyDataErrorInfo)section).ErrorsChanged += (_, e) => changedProps.Add(e.PropertyName);
        section.RunAllValidations();

        // Port error should be re-raised so the new binding can see it
        Assert.Contains(nameof(IServerConfigSettings.Port), changedProps);
        var errors = ((INotifyDataErrorInfo)section)
            .GetErrors(nameof(IServerConfigSettings.Port)).Cast<string>().ToList();
        Assert.Contains("Port must be between 1 and 65535.", errors);
    }
}
