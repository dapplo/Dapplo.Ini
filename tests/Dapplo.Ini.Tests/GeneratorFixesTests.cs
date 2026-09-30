// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Dapplo.Ini;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests
{
    /// <summary>A section interface used as the base of another section interface.</summary>
    public interface IBaseWithPropertySettings : IIniSection
    {
        [IniValue(DefaultValue = "base")]
        string? FromBase { get; set; }
    }

    /// <summary>Derived section: the generated class must implement the inherited property too.</summary>
    [IniSection("Derived")]
    public interface IDerivedWithBaseSettings : IBaseWithPropertySettings
    {
        [IniValue(DefaultValue = "derived")]
        string? FromDerived { get; set; }

        /// <summary>A static property is not a setting and must be ignored by the generator.</summary>
        static string Kind => "derived";
    }

    /// <summary>Generic (static-virtual) async lifecycle hooks.</summary>
    [IniSection("AsyncGeneric")]
    public interface IAsyncGenericHookSettings
        : IIniSection,
          IAfterLoadAsync<IAsyncGenericHookSettings>,
          IBeforeSaveAsync<IAsyncGenericHookSettings>,
          IAfterSaveAsync<IAsyncGenericHookSettings>
    {
        string? Value { get; set; }

        [IgnoreDataMember] bool AfterLoadCalled { get; set; }
        [IgnoreDataMember] bool BeforeSaveCalled { get; set; }
        [IgnoreDataMember] bool AfterSaveCalled { get; set; }

        static new ValueTask OnAfterLoadAsync(IAsyncGenericHookSettings self, CancellationToken cancellationToken)
        {
            self.AfterLoadCalled = true;
            return default;
        }

        static new ValueTask<bool> OnBeforeSaveAsync(IAsyncGenericHookSettings self, CancellationToken cancellationToken)
        {
            self.BeforeSaveCalled = true;
            return new ValueTask<bool>(true);
        }

        static new ValueTask OnAfterSaveAsync(IAsyncGenericHookSettings self, CancellationToken cancellationToken)
        {
            self.AfterSaveCalled = true;
            return default;
        }
    }

    /// <summary>Default values given with the Type-based and array forms of [DefaultValue].</summary>
    [IniSection("DefaultForms")]
    public interface IDefaultFormsSettings : IIniSection
    {
        [DefaultValue(typeof(TimeSpan), "00:01:30")]
        TimeSpan Interval { get; set; }

        [DefaultValue(new[] { "a", "b" })]
        List<string>? Items { get; set; }
    }

    /// <summary>Validation attributes on property types that used to generate broken code.</summary>
    [IniSection("ValidationForms")]
    public interface IValidationFormsSettings : IIniSection
    {
        [Range(1, 100)]
        [IniValue(DefaultValue = "50")]
        double Ratio { get; set; }

        [Range(typeof(decimal), "1", "10")]
        [IniValue(DefaultValue = "5")]
        decimal Price { get; set; }

        [Range(1, 10)]
        int? Optional { get; set; }

        [MaxLength(2)]
        List<string>? Tags { get; set; }

        [RegularExpression("^[0-9]{2}$")]
        [IniValue(DefaultValue = "10")]
        int TwoDigits { get; set; }

        [Required]
        [IniValue(DefaultValue = "x")]
        string? Name { get; set; }
    }

    /// <summary>Non-generic IDataValidation implemented in a partial class, combined with attributes.</summary>
    [IniSection("PlainValidation")]
    public interface IPlainValidationSettings : IIniSection, IDataValidation
    {
        [Range(1, 10)]
        [IniValue(DefaultValue = "5")]
        int Level { get; set; }

        [IniValue(DefaultValue = "ok")]
        string? Code { get; set; }
    }

    /// <summary>The natural way to implement the non-generic interface: a public method.</summary>
    public partial class PlainValidationSettingsImpl
    {
        public IEnumerable<string> ValidateProperty(string propertyName)
        {
            if (propertyName == nameof(Code) && Code == "bad")
                yield return "Code must not be 'bad'.";
        }
    }

    /// <summary>An interface without the "I" prefix: its name must be kept as-is.</summary>
    public interface Interval : IIniSection
    {
        string? Value { get; set; }
    }

    /// <summary>A section interface nested in a class.</summary>
    public static class SettingsContainer
    {
        [IniSection("Nested")]
        public interface INestedSettings : IIniSection
        {
            string? Value { get; set; }
        }
    }

    /// <summary>Tests for the source generator fixes.</summary>
    [Collection("IniConfigRegistry")]
    public sealed class GeneratorFixesTests : IDisposable
    {
        private readonly string _tempDir;

        public GeneratorFixesTests()
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

        private string WriteIni(string fileName, string content)
        {
            var path = Path.Combine(_tempDir, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void DerivedInterface_ImplementsInheritedProperties()
        {
            WriteIni("derived.ini", "[Derived]\nFromBase = file");
            var section = new DerivedWithBaseSettingsImpl();
            using var config = IniConfigRegistry.ForFile("derived.ini")
                .AddSearchPath(_tempDir)
                .RegisterSection<IDerivedWithBaseSettings>(section)
                .Build();

            Assert.Equal("file", section.FromBase);
            Assert.Equal("derived", section.FromDerived);
            Assert.Contains("FromBase", section.GetKeys());
        }

        [Fact]
        public async Task GenericAsyncHooks_AreCalled()
        {
            var section = new AsyncGenericHookSettingsImpl();
            var config = await IniConfigRegistry.ForFile("async-generic.ini")
                .AddSearchPath(_tempDir)
                .RegisterSection<IAsyncGenericHookSettings>(section)
                .BuildAsync();

            await config.SaveAsync();

            Assert.True(section.AfterLoadCalled);
            Assert.True(section.BeforeSaveCalled);
            Assert.True(section.AfterSaveCalled);
            config.Dispose();
        }

        [Fact]
        public void DefaultValue_TypeAndArrayForms_AreApplied()
        {
            var section = new DefaultFormsSettingsImpl();
            section.ResetToDefaults();

            Assert.Equal(TimeSpan.FromSeconds(90), section.Interval);
            Assert.Equal(new[] { "a", "b" }, section.Items);
        }

        [Fact]
        public void Validation_RangeMaxLengthRegexAndRequired_WorkOnAllTypes()
        {
            var section = new ValidationFormsSettingsImpl();
            using var config = IniConfigRegistry.ForFile("validation-forms.ini")
                .AddSearchPath(_tempDir)
                .RegisterSection<IValidationFormsSettings>(section)
                .Build();
            var errors = (INotifyDataErrorInfo)section;

            Assert.False(errors.HasErrors);

            section.Ratio = 500;
            section.Price = 11m;
            section.Optional = 0;
            section.Tags = new List<string> { "a", "b", "c" };
            section.TwoDigits = 5;
            section.Name = "   ";

            Assert.NotEmpty(errors.GetErrors(nameof(IValidationFormsSettings.Ratio)).Cast<object>());
            Assert.NotEmpty(errors.GetErrors(nameof(IValidationFormsSettings.Price)).Cast<object>());
            Assert.NotEmpty(errors.GetErrors(nameof(IValidationFormsSettings.Optional)).Cast<object>());
            Assert.NotEmpty(errors.GetErrors(nameof(IValidationFormsSettings.Tags)).Cast<object>());
            Assert.NotEmpty(errors.GetErrors(nameof(IValidationFormsSettings.TwoDigits)).Cast<object>());
            Assert.NotEmpty(errors.GetErrors(nameof(IValidationFormsSettings.Name)).Cast<object>());

            section.Optional = null;
            Assert.Empty(errors.GetErrors(nameof(IValidationFormsSettings.Optional)).Cast<object>());
        }

        [Fact]
        public void NonGenericDataValidation_WithAttributes_CombinesBothRuleSets()
        {
            var section = new PlainValidationSettingsImpl();
            using var config = IniConfigRegistry.ForFile("plain-validation.ini")
                .AddSearchPath(_tempDir)
                .RegisterSection<IPlainValidationSettings>(section)
                .Build();
            var errors = (INotifyDataErrorInfo)section;

            section.Level = 50;
            section.Code = "bad";

            Assert.NotEmpty(errors.GetErrors(nameof(IPlainValidationSettings.Level)).Cast<object>());
            Assert.Equal(new object[] { "Code must not be 'bad'." }, errors.GetErrors(nameof(IPlainValidationSettings.Code)).Cast<object>());
        }

        [Fact]
        public void InterfaceWithoutIPrefix_KeepsItsName()
        {
            var section = new IntervalImpl();
            Assert.Equal("Interval", section.SectionName);
        }

        [Fact]
        public void NestedInterface_IsGenerated()
        {
            var section = new NestedSettingsImpl();
            Assert.IsAssignableFrom<SettingsContainer.INestedSettings>(section);
            Assert.Equal("Nested", section.SectionName);
        }

        [Fact]
        public void SameInterfaceNameInAnotherNamespace_IsGenerated()
        {
            var section = new Other.HostSettingsImpl();
            Assert.Equal("OtherHost", section.SectionName);
        }

        [Fact]
        public void Reload_RaisesPropertyChangedForChangedValues()
        {
            var path = WriteIni("npc-reload.ini", "[General]\nAppName = before\nMaxRetries = 1");
            var section = new GeneralSettingsImpl();
            using var config = IniConfigRegistry.ForFile("npc-reload.ini")
                .AddSearchPath(_tempDir)
                .RegisterSection<IGeneralSettings>(section)
                .Build();
            var changed = new List<string?>();
            section.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            File.WriteAllText(path, "[General]\nAppName = after\nMaxRetries = 1");
            config.Reload();

            Assert.Equal(new[] { nameof(IGeneralSettings.AppName) }, changed);
        }
    }
}

namespace Dapplo.Ini.Tests.Other
{
    /// <summary>Same simple name as <see cref="Dapplo.Ini.Tests.IHostSettings"/>, in another namespace.</summary>
    [IniSection("OtherHost")]
    public interface IHostSettings : IIniSection
    {
        string? Value { get; set; }
    }
}
