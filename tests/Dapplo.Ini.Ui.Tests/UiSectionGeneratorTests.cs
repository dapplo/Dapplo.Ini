// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Ui.Enums;
using Dapplo.Ini.Ui.Metadata;

namespace Dapplo.Ini.Ui.Tests;

/// <summary>
/// Tests that verify the output of the <c>UiSectionGenerator</c> source generator.
/// Each <c>*UiDescriptor.Page</c> static property is generated at compile time from
/// the UI attributes on the corresponding section interface.
/// </summary>
public sealed class UiSectionGeneratorTests
{
    // ── AppearanceUiSettings descriptor ───────────────────────────────────────

    [Fact]
    public void AppearanceDescriptor_HasCorrectTitle()
    {
        Assert.Equal("Appearance", AppearanceUiSettingsUiDescriptor.Page.Title);
    }

    [Fact]
    public void AppearanceDescriptor_HasCorrectSectionType()
    {
        Assert.Equal(typeof(IAppearanceUiSettings), AppearanceUiSettingsUiDescriptor.Page.SectionType);
    }

    [Fact]
    public void AppearanceDescriptor_DefaultChangeModeIsImmediate()
    {
        Assert.Equal(UiChangeMode.Immediate, AppearanceUiSettingsUiDescriptor.Page.DefaultChangeMode);
    }

    [Fact]
    public void AppearanceDescriptor_ContainsThreeProperties()
    {
        Assert.Equal(3, AppearanceUiSettingsUiDescriptor.Page.Properties.Count);
    }

    [Fact]
    public void AppearanceDescriptor_DarkModeIsCheckBox()
    {
        var darkMode = AppearanceUiSettingsUiDescriptor.Page.Properties
            .First(p => p.PropertyName == nameof(IAppearanceUiSettings.DarkMode));
        Assert.Equal(UiControlType.CheckBox, darkMode.ControlType);
    }

    [Fact]
    public void AppearanceDescriptor_AccentColorIsDropDown()
    {
        var accent = AppearanceUiSettingsUiDescriptor.Page.Properties
            .First(p => p.PropertyName == nameof(IAppearanceUiSettings.AccentColor));
        Assert.Equal(UiControlType.DropDown, accent.ControlType);
        Assert.Equal("Theme", accent.GroupName);
    }

    [Fact]
    public void AppearanceDescriptor_FontSizeHasEnableCondition()
    {
        var fontSize = AppearanceUiSettingsUiDescriptor.Page.Properties
            .First(p => p.PropertyName == nameof(IAppearanceUiSettings.FontSize));
        Assert.Equal(nameof(IAppearanceUiSettings.DarkMode), fontSize.EnableConditionProperty);
        Assert.True(fontSize.InvertEnable);
    }

    [Fact]
    public void AppearanceDescriptor_LabelKeyForDarkMode()
    {
        var darkMode = AppearanceUiSettingsUiDescriptor.Page.Properties
            .First(p => p.PropertyName == nameof(IAppearanceUiSettings.DarkMode));
        Assert.Equal("ui_dark_mode_label", darkMode.LabelKey);
        Assert.Equal("ui_dark_mode_tooltip", darkMode.DescriptionKey);
    }

    // ── NetworkUiSettings descriptor ──────────────────────────────────────────

    [Fact]
    public void NetworkDescriptor_HasCorrectTitle()
    {
        Assert.Equal("Network", NetworkUiSettingsUiDescriptor.Page.Title);
    }

    [Fact]
    public void NetworkDescriptor_HasAdvancedCategory()
    {
        Assert.Equal("Advanced", NetworkUiSettingsUiDescriptor.Page.Category);
    }

    [Fact]
    public void NetworkDescriptor_OrderIsTen()
    {
        Assert.Equal(10, NetworkUiSettingsUiDescriptor.Page.Order);
    }

    [Fact]
    public void NetworkDescriptor_DefaultChangeModeIsOnConfirm()
    {
        Assert.Equal(UiChangeMode.OnConfirm, NetworkUiSettingsUiDescriptor.Page.DefaultChangeMode);
    }

    [Fact]
    public void NetworkDescriptor_ProxyHostHasVisibilityCondition()
    {
        var host = NetworkUiSettingsUiDescriptor.Page.Properties
            .First(p => p.PropertyName == nameof(INetworkUiSettings.ProxyHost));
        Assert.Equal(nameof(INetworkUiSettings.UseProxy), host.VisibilityConditionProperty);
        Assert.Equal(nameof(INetworkUiSettings.UseProxy), host.EnableConditionProperty);
        Assert.Equal("Proxy", host.GroupName);
    }

    [Fact]
    public void NetworkDescriptor_ProxyPortIsUpDown()
    {
        var port = NetworkUiSettingsUiDescriptor.Page.Properties
            .First(p => p.PropertyName == nameof(INetworkUiSettings.ProxyPort));
        Assert.Equal(UiControlType.UpDown, port.ControlType);
    }

    // ── VolumeUiSettings descriptor ───────────────────────────────────────────

    [Fact]
    public void VolumeDescriptor_HasSoundTitle()
    {
        Assert.Equal("Sound", VolumeUiSettingsUiDescriptor.Page.Title);
    }

    [Fact]
    public void VolumeDescriptor_VolumeIsSlider()
    {
        var volume = VolumeUiSettingsUiDescriptor.Page.Properties
            .First(p => p.PropertyName == nameof(IVolumeUiSettings.Volume));
        Assert.Equal(UiControlType.Slider, volume.ControlType);
    }

    [Fact]
    public void VolumeDescriptor_MutedIsCheckBox()
    {
        var muted = VolumeUiSettingsUiDescriptor.Page.Properties
            .First(p => p.PropertyName == nameof(IVolumeUiSettings.Muted));
        Assert.Equal(UiControlType.CheckBox, muted.ControlType);
    }

    [Fact]
    public void VolumeDescriptor_OutputModeIsRadioButton()
    {
        var mode = VolumeUiSettingsUiDescriptor.Page.Properties
            .First(p => p.PropertyName == nameof(IVolumeUiSettings.OutputMode));
        Assert.Equal(UiControlType.RadioButton, mode.ControlType);
    }

    // ── Compile-time vs runtime metadata equivalence ──────────────────────────

    [Fact]
    public void GeneratedDescriptor_MatchesRuntimeMetadataReader_ForNetwork()
    {
        var generated = NetworkUiSettingsUiDescriptor.Page;
        var runtime = UiMetadataReader.ReadPage<INetworkUiSettings>();

        Assert.Equal(runtime.Title, generated.Title);
        Assert.Equal(runtime.Category, generated.Category);
        Assert.Equal(runtime.Order, generated.Order);
        Assert.Equal(runtime.DefaultChangeMode, generated.DefaultChangeMode);
        Assert.Equal(runtime.Properties.Count, generated.Properties.Count);

        foreach (var rp in runtime.Properties)
        {
            var gp = generated.Properties.First(p => p.PropertyName == rp.PropertyName);
            Assert.Equal(rp.ControlType, gp.ControlType);
            Assert.Equal(rp.GroupName, gp.GroupName);
            Assert.Equal(rp.VisibilityConditionProperty, gp.VisibilityConditionProperty);
            Assert.Equal(rp.EnableConditionProperty, gp.EnableConditionProperty);
            Assert.Equal(rp.ChangeMode, gp.ChangeMode);
        }
    }
}
