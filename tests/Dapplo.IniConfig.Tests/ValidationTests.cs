// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using Dapplo.IniConfig.Configuration;
using Dapplo.IniConfig.Interfaces;

namespace Dapplo.IniConfig.Tests;

/// <summary>
/// Tests for the <see cref="IDataValidation{TSelf}"/> interface and
/// <see cref="System.ComponentModel.INotifyDataErrorInfo"/> integration.
/// </summary>
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
}
