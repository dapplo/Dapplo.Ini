// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using Dapplo.Ini;

namespace Dapplo.Ini.Tests;

/// <summary>
/// Tests that verify the source generator honours standard .NET attributes:
/// <list type="bullet">
///   <item><c>[DataMember(Name=...)]</c> sets the INI key name for a property.</item>
///   <item><c>[DefaultValue(...)]</c> sets the default value for a property.</item>
///   <item><c>[IgnoreDataMember]</c> excludes a property from INI read/write.</item>
///   <item><c>[Required]</c>, <c>[Range]</c>, <c>[MaxLength]</c> generate validation rules that
///         surface errors via <c>INotifyDataErrorInfo</c> without throwing exceptions.</item>
/// </list>
/// Note: <c>[DataContract(Name=...)]</c> cannot be applied to interface declarations in .NET
/// and is therefore not tested here; the generator does contain fallback logic for it in
/// case the attribute becomes applicable in a future scenario.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class StandardAttributeTests : IDisposable
{
    private readonly string _tempDir;

    public StandardAttributeTests()
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

    private string WriteIni(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    // ── [Description] on interface ────────────────────────────────────────────

    [Fact]
    public void StandardSection_HasCorrectSectionName()
    {
        // [IniSection("StandardSection")] should be the section name
        var section = new StandardAttributeSettingsImpl();
        Assert.Equal("StandardSection", section.SectionName);
    }

    // ── [DataMember] key name ─────────────────────────────────────────────────

    [Fact]
    public void DataMember_Name_IsUsedAsIniKey()
    {
        // Write an INI file using the [DataMember(Name="display_name")] key
        WriteIni("datamember.ini", "[StandardSection]\ndisplay_name = Hello");

        var section = new StandardAttributeSettingsImpl();
        IniConfigRegistry.ForFile("datamember.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IStandardAttributeSettings>(section)
            .Build();

        Assert.Equal("Hello", section.DisplayName);
    }

    [Fact]
    public void DataMember_Name_IsWrittenAsIniKey()
    {
        var path = Path.Combine(_tempDir, "write_dm.ini");
        var section = new StandardAttributeSettingsImpl();
        var config = IniConfigRegistry.ForFile("write_dm.ini")
            .AddSearchPath(_tempDir)
            .SetWritablePath(path)
            .RegisterSection<IStandardAttributeSettings>(section)
            .Build();

        section.DisplayName = "Test";
        config.Save();

        var content = File.ReadAllText(path);
        Assert.Contains("display_name = Test", content);
    }

    // ── [DefaultValue] ────────────────────────────────────────────────────────

    [Fact]
    public void DefaultValue_StringAttribute_SetsDefault()
    {
        var section = new StandardAttributeSettingsImpl();
        IniConfigRegistry.ForFile("default_str.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IStandardAttributeSettings>(section)
            .Build();

        // [DefaultValue("World")] on DisplayName
        Assert.Equal("World", section.DisplayName);
    }

    [Fact]
    public void DefaultValue_IntAttribute_SetsDefault()
    {
        var section = new StandardAttributeSettingsImpl();
        IniConfigRegistry.ForFile("default_int.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IStandardAttributeSettings>(section)
            .Build();

        // [DefaultValue(10)] on RetryCount
        Assert.Equal(10, section.RetryCount);
    }

    // ── [IniValue] takes precedence over [DataMember] ────────────────────────

    [Fact]
    public void IniValue_KeyName_TakesPrecedenceOverDataMember()
    {
        // [IniValue(KeyName="ini_key")] on Precedence property should win over [DataMember(Name="data_member_key")]
        WriteIni("prec.ini", "[StandardSection]\nini_key = PrecValue");

        var section = new StandardAttributeSettingsImpl();
        IniConfigRegistry.ForFile("prec.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IStandardAttributeSettings>(section)
            .Build();

        Assert.Equal("PrecValue", section.Precedence);
    }

    // ── [IgnoreDataMember] ────────────────────────────────────────────────────

    [Fact]
    public void IgnoreDataMember_PropertyIsNotLoadedFromIni()
    {
        // Even though the INI file has a value for "Transient", it should be ignored
        WriteIni("ignore.ini", "[StandardSection]\nTransient = SomeValue");

        var section = new StandardAttributeSettingsImpl();
        IniConfigRegistry.ForFile("ignore.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IStandardAttributeSettings>(section)
            .Build();

        // Transient is excluded — it keeps its default (null) regardless of the file
        Assert.Null(section.Transient);
    }

    [Fact]
    public void IgnoreDataMember_PropertyIsNotWrittenToIni()
    {
        var path = Path.Combine(_tempDir, "ignore_write.ini");
        var section = new StandardAttributeSettingsImpl();
        var config = IniConfigRegistry.ForFile("ignore_write.ini")
            .AddSearchPath(_tempDir)
            .SetWritablePath(path)
            .RegisterSection<IStandardAttributeSettings>(section)
            .Build();

        // Setting Transient should not appear in the saved file
        section.Transient = "ShouldNotBeSaved";
        config.Save();

        var content = File.ReadAllText(path);
        Assert.DoesNotContain("Transient", content);
        Assert.DoesNotContain("ShouldNotBeSaved", content);
    }

    // ── [Required] validation ─────────────────────────────────────────────────

    [Fact]
    public void Required_NullValue_HasErrors()
    {
        var section = new AnnotatedSettingsImpl();
        IniConfigRegistry.ForFile("req.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAnnotatedSettings>(section)
            .Build();

        section.Name = null;
        var errors = ((INotifyDataErrorInfo)section)
            .GetErrors(nameof(IAnnotatedSettings.Name)).Cast<string>().ToList();
        Assert.NotEmpty(errors);
        Assert.Contains("Name is required.", errors);
    }

    [Fact]
    public void Required_EmptyString_HasErrors()
    {
        var section = new AnnotatedSettingsImpl();
        IniConfigRegistry.ForFile("req_empty.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAnnotatedSettings>(section)
            .Build();

        section.Name = "";
        Assert.True(((INotifyDataErrorInfo)section).HasErrors);
    }

    [Fact]
    public void Required_ValidValue_HasNoErrors()
    {
        var section = new AnnotatedSettingsImpl();
        IniConfigRegistry.ForFile("req_valid.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAnnotatedSettings>(section)
            .Build();

        section.Name = "Alice";
        Assert.Empty(((INotifyDataErrorInfo)section)
            .GetErrors(nameof(IAnnotatedSettings.Name)).Cast<string>());
    }

    // ── [Range] validation ────────────────────────────────────────────────────

    [Fact]
    public void Range_BelowMin_HasErrors()
    {
        var section = new AnnotatedSettingsImpl();
        IniConfigRegistry.ForFile("range_low.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAnnotatedSettings>(section)
            .Build();

        section.Score = 0; // below 1
        var errors = ((INotifyDataErrorInfo)section)
            .GetErrors(nameof(IAnnotatedSettings.Score)).Cast<string>().ToList();
        Assert.Contains("Score must be between 1 and 100.", errors);
    }

    [Fact]
    public void Range_AboveMax_HasErrors()
    {
        var section = new AnnotatedSettingsImpl();
        IniConfigRegistry.ForFile("range_high.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAnnotatedSettings>(section)
            .Build();

        section.Score = 101; // above 100
        Assert.True(((INotifyDataErrorInfo)section).HasErrors);
    }

    [Fact]
    public void Range_ValidValue_HasNoErrors()
    {
        var section = new AnnotatedSettingsImpl();
        IniConfigRegistry.ForFile("range_valid.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAnnotatedSettings>(section)
            .Build();

        section.Score = 50;
        Assert.Empty(((INotifyDataErrorInfo)section)
            .GetErrors(nameof(IAnnotatedSettings.Score)).Cast<string>());
    }

    // ── [MaxLength] validation ────────────────────────────────────────────────

    [Fact]
    public void MaxLength_TooLong_HasErrors()
    {
        var section = new AnnotatedSettingsImpl();
        IniConfigRegistry.ForFile("maxlen.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAnnotatedSettings>(section)
            .Build();

        section.Tag = new string('x', 21); // 21 chars > 20
        var errors = ((INotifyDataErrorInfo)section)
            .GetErrors(nameof(IAnnotatedSettings.Tag)).Cast<string>().ToList();
        Assert.Contains("Tag must not exceed 20 characters.", errors);
    }

    [Fact]
    public void MaxLength_WithinLimit_HasNoErrors()
    {
        var section = new AnnotatedSettingsImpl();
        IniConfigRegistry.ForFile("maxlen_ok.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAnnotatedSettings>(section)
            .Build();

        section.Tag = "short";
        Assert.Empty(((INotifyDataErrorInfo)section)
            .GetErrors(nameof(IAnnotatedSettings.Tag)).Cast<string>());
    }

    // ── Validation after load ─────────────────────────────────────────────────

    [Fact]
    public void Validation_RunsAfterLoad_WhenFileContainsInvalidData()
    {
        // An invalid Score value (0, below min=1) loaded from file should produce errors
        WriteIni("after_load.ini", "[AnnotatedSection]\nScore = 0\nName = Alice");

        var section = new AnnotatedSettingsImpl();
        IniConfigRegistry.ForFile("after_load.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAnnotatedSettings>(section)
            .Build();

        var errors = ((INotifyDataErrorInfo)section)
            .GetErrors(nameof(IAnnotatedSettings.Score)).Cast<string>().ToList();
        Assert.NotEmpty(errors);
        Assert.Contains("Score must be between 1 and 100.", errors);
    }

    [Fact]
    public void Validation_RunsAfterLoad_NoErrorsWhenFileIsValid()
    {
        WriteIni("after_load_valid.ini", "[AnnotatedSection]\nScore = 50\nName = Alice\nTag = ok");

        var section = new AnnotatedSettingsImpl();
        IniConfigRegistry.ForFile("after_load_valid.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAnnotatedSettings>(section)
            .Build();

        Assert.False(((INotifyDataErrorInfo)section).HasErrors);
    }

    // ── Combined DataAnnotations + IDataValidation<TSelf> ─────────────────────

    [Fact]
    public void Combined_AttributeAndCustomRules_BothEnforced()
    {
        var section = new CombinedValidationSettingsImpl();
        IniConfigRegistry.ForFile("combined.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICombinedValidationSettings>(section)
            .Build();

        // Trigger [Required] rule
        section.Host = null;
        var requiredErrors = ((INotifyDataErrorInfo)section)
            .GetErrors(nameof(ICombinedValidationSettings.Host)).Cast<string>().ToList();
        Assert.Contains("Host is required.", requiredErrors);

        // Trigger custom rule ("banned" is disallowed)
        section.Host = "banned";
        var customErrors = ((INotifyDataErrorInfo)section)
            .GetErrors(nameof(ICombinedValidationSettings.Host)).Cast<string>().ToList();
        Assert.Contains("Host value 'banned' is not allowed.", customErrors);

        // Valid host — no errors
        section.Host = "localhost";
        Assert.Empty(((INotifyDataErrorInfo)section)
            .GetErrors(nameof(ICombinedValidationSettings.Host)).Cast<string>());
    }

    [Fact]
    public void Combined_Port_RangeValidation()
    {
        var section = new CombinedValidationSettingsImpl();
        IniConfigRegistry.ForFile("combined_port.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICombinedValidationSettings>(section)
            .Build();

        section.Port = 0; // below range
        Assert.True(((INotifyDataErrorInfo)section).HasErrors);

        section.Port = 8080; // valid
        Assert.Empty(((INotifyDataErrorInfo)section)
            .GetErrors(nameof(ICombinedValidationSettings.Port)).Cast<string>());
    }

    // ── No exceptions thrown on invalid data ──────────────────────────────────

    [Fact]
    public void InvalidData_DoesNotThrow_JustSetsErrors()
    {
        WriteIni("no_throw.ini", "[AnnotatedSection]\nScore = -999\nName =");

        var section = new AnnotatedSettingsImpl();
        // Must not throw — errors are surfaced via INotifyDataErrorInfo instead
        var ex = Record.Exception(() =>
            IniConfigRegistry.ForFile("no_throw.ini")
                .AddSearchPath(_tempDir)
                .RegisterSection<IAnnotatedSettings>(section)
                .Build());

        Assert.Null(ex);
        Assert.True(((INotifyDataErrorInfo)section).HasErrors);
    }
}
