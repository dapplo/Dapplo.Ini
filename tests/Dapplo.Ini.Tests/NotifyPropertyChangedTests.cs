// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

public sealed class NotifyPropertyChangedTests
{
    // ── Basic event raising ───────────────────────────────────────────────────

    [Fact]
    public void SettingProperty_RaisesPropertyChangedEvent()
    {
        var section = new GeneralSettingsImpl();
        section.ResetToDefaults();

        var changedNames = new List<string>();
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedNames.Add(e.PropertyName);
        };

        section.AppName = "NewName";

        Assert.Contains("AppName", changedNames);
    }

    [Fact]
    public void SettingProperty_InterfaceExtendsINotifyPropertyChanging_RaisesPropertyChangingEvent()
    {
        // IGeneralSettings extends only INotifyPropertyChanged, not INotifyPropertyChanging,
        // so PropertyChanging should NOT fire.  Use the dedicated NPC test interface below.
        var section = new NpcBothEventsSettingsImpl();
        section.ResetToDefaults();

        var changingNames = new List<string>();
        section.PropertyChanging += (_, e) =>
        {
            if (e.PropertyName != null) changingNames.Add(e.PropertyName);
        };

        section.Name = "AnotherName";

        Assert.Contains("Name", changingNames);
    }

    [Fact]
    public void SettingPropertyToSameValue_DoesNotRaiseEvent()
    {
        var section = new GeneralSettingsImpl();
        section.ResetToDefaults();
        section.AppName = "MyApp"; // same as default

        int eventCount = 0;
        section.PropertyChanged += (_, _) => eventCount++;
        section.AppName = "MyApp"; // no change

        Assert.Equal(0, eventCount);
    }

    // ── All properties fire events when interface extends INotifyPropertyChanged ─

    [Fact]
    public void AllProperties_FirePropertyChangedEvents_WhenInterfaceExtendsINPC()
    {
        // IGeneralSettings extends INotifyPropertyChanged → all properties raise events.
        var section = new GeneralSettingsImpl();
        section.ResetToDefaults();

        var changedNames = new List<string>();
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedNames.Add(e.PropertyName);
        };

        section.AppName      = "Foo";
        section.MaxRetries   = 99;
        section.EnableLogging = false;
        section.Threshold    = 9.99;

        Assert.Contains("AppName",      changedNames);
        Assert.Contains("MaxRetries",   changedNames);
        Assert.Contains("EnableLogging", changedNames);
        Assert.Contains("Threshold",    changedNames);
    }

    // ── SuppressPropertyChanged / SuppressPropertyChanging ────────────────────

    [Fact]
    public void SuppressPropertyChanged_PreventsSinglePropertyFromFiringEvent()
    {
        var section = new NpcBothEventsSettingsImpl();
        section.ResetToDefaults();

        var changedNames = new List<string>();
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedNames.Add(e.PropertyName);
        };

        // "Name" fires; "SilentValue" has SuppressPropertyChanged=true so it should NOT fire.
        section.Name        = "Foo";
        section.SilentValue = "Bar";

        Assert.Contains("Name",   changedNames);
        Assert.DoesNotContain("SilentValue", changedNames);
    }

    [Fact]
    public void SuppressPropertyChanging_PreventsSinglePropertyFromFiringChangingEvent()
    {
        var section = new NpcBothEventsSettingsImpl();
        section.ResetToDefaults();

        var changingNames = new List<string>();
        section.PropertyChanging += (_, e) =>
        {
            if (e.PropertyName != null) changingNames.Add(e.PropertyName);
        };

        // "Name" fires Changing; "NoChangingValue" has SuppressPropertyChanging=true.
        section.Name           = "Foo";
        section.NoChangingValue = "Bar";

        Assert.Contains("Name", changingNames);
        Assert.DoesNotContain("NoChangingValue", changingNames);
    }

    // ── Interface without INotifyPropertyChanged generates no events ──────────

    [Fact]
    public void Interface_WithoutINPC_GeneratedClass_DoesNotImplementINPC()
    {
        // IUserSettings does NOT extend INotifyPropertyChanged, so the generated class
        // should not implement it either (better performance, less memory).
        var section = new UserSettingsImpl();
        Assert.False(section is INotifyPropertyChanged,
            "Generated class for a non-INotifyPropertyChanged interface must not implement INPC.");
    }

    [Fact]
    public void PartialSetHook_CanCoerceValueBeforeStorage_AndRaiseSingleChangedEvent()
    {
        var section = new RuntimeCoercionSettingsImpl();
        section.ResetToDefaults();

        var changedNames = new List<string>();
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedNames.Add(e.PropertyName);
        };

        section.Port = 70000;

        Assert.Equal(65535, section.Port);
        Assert.Equal(1, changedNames.Count(n => n == nameof(IRuntimeCoercionSettings.Port)));
    }

    [Fact]
    public void PartialSetHook_EqualityCheckUsesCoercedValue()
    {
        var section = new RuntimeCoercionSettingsImpl();
        section.ResetToDefaults();
        section.Port = 65535;

        int eventCount = 0;
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IRuntimeCoercionSettings.Port))
                eventCount++;
        };

        // Coerces to 65535, which equals the current value, so no event should fire.
        section.Port = 70000;
        Assert.Equal(0, eventCount);
    }
}

// ── Sample interfaces for NPC suppression tests ──────────────────────────────

/// <summary>
/// Section that extends both INotifyPropertyChanged and INotifyPropertyChanging
/// to test per-property event suppression.
/// </summary>
[IniSection("NpcBothEvents")]
public interface INpcBothEventsSettings
    : IIniSection, INotifyPropertyChanged, INotifyPropertyChanging
{
    [IniValue(DefaultValue = "initial")]
    string? Name { get; set; }

    /// <summary>PropertyChanged is suppressed for this property.</summary>
    [IniValue(DefaultValue = "silent", SuppressPropertyChanged = true)]
    string? SilentValue { get; set; }

    /// <summary>PropertyChanging is suppressed for this property.</summary>
    [IniValue(DefaultValue = "noChanging", SuppressPropertyChanging = true)]
    string? NoChangingValue { get; set; }
}

[IniSection("RuntimeCoercion")]
public interface IRuntimeCoercionSettings : IIniSection, INotifyPropertyChanged
{
    [IniValue(DefaultValue = "8080")]
    int Port { get; set; }
}

public partial class RuntimeCoercionSettingsImpl
{
    partial void OnPortSet(ref int value) => value = Math.Clamp(value, 1, 65535);
}
