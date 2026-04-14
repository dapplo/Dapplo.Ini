// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Ui.Attributes;
using Dapplo.Ini.Ui.Enums;
using Dapplo.Ini.Ui.Metadata;

namespace Dapplo.Ini.Ui.Tests;

// ── Sample interfaces ────────────────────────────────────────────────────────

[IniSection("AppearanceUi")]
[UiPage(Title = "Appearance", Order = 0)]
[UiChangeMode(UiChangeMode.Immediate)]
public interface IAppearanceUiSettings : IIniSection
{
    [DefaultValue(false)]
    [UiLabelKey("ui_dark_mode_label", DescriptionKey = "ui_dark_mode_tooltip")]
    bool DarkMode { get; set; }

    [DefaultValue("Blue")]
    [UiControl(UiControlType.DropDown)]
    [UiGroup("Theme", Order = 10)]
    [UiLabelKey("ui_accent_color_label")]
    string AccentColor { get; set; }

    [DefaultValue(12)]
    [UiControl(UiControlType.UpDown, Minimum = 8, Maximum = 72, Increment = 2)]
    [UiGroup("Theme")]
    [UiConditionalEnable(nameof(DarkMode), Invert = true)]
    int FontSize { get; set; }
}

[IniSection("NetworkUi")]
[UiPage(Title = "Network", Category = "Advanced", Order = 10)]
[UiChangeMode(UiChangeMode.OnConfirm)]
public interface INetworkUiSettings : IIniSection, ITransactional
{
    [DefaultValue(false)]
    bool UseProxy { get; set; }

    [DefaultValue("")]
    [UiConditionalVisibility(nameof(UseProxy))]
    [UiConditionalEnable(nameof(UseProxy))]
    [UiGroup("Proxy", Order = 5)]
    string ProxyHost { get; set; }

    [DefaultValue(8080)]
    [UiControl(UiControlType.UpDown, Minimum = 1, Maximum = 65535)]
    [UiConditionalVisibility(nameof(UseProxy))]
    [UiConditionalEnable(nameof(UseProxy))]
    [UiGroup("Proxy")]
    int ProxyPort { get; set; }
}

[IniSection("VolumeUi")]
[UiPage(Title = "Sound")]
public interface IVolumeUiSettings : IIniSection
{
    [DefaultValue(50)]
    [UiControl(UiControlType.Slider, Minimum = 0, Maximum = 100)]
    int Volume { get; set; }

    [DefaultValue(false)]
    bool Muted { get; set; }

    [DefaultValue("Stereo")]
    [UiControl(UiControlType.RadioButton)]
    string OutputMode { get; set; }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Tests for the UI configuration framework attributes and metadata reader.
/// </summary>
public sealed class UiAttributeTests
{
    // ── UiPageAttribute ───────────────────────────────────────────────────────

    [Fact]
    public void UiPageAttribute_ReadsTitle()
    {
        var page = UiMetadataReader.ReadPage<IAppearanceUiSettings>();
        Assert.Equal("Appearance", page.Title);
    }

    [Fact]
    public void UiPageAttribute_ReadsOrder()
    {
        var page = UiMetadataReader.ReadPage<INetworkUiSettings>();
        Assert.Equal(10, page.Order);
    }

    [Fact]
    public void UiPageAttribute_ReadsCategory()
    {
        var page = UiMetadataReader.ReadPage<INetworkUiSettings>();
        Assert.Equal("Advanced", page.Category);
    }

    [Fact]
    public void UiPageAttribute_DefaultTitleFromInterfaceName_WhenNoAttribute()
    {
        // IVolumeUiSettings has UiPage with no Title -> uses attribute default
        var page = UiMetadataReader.ReadPage<IVolumeUiSettings>();
        Assert.Equal("Sound", page.Title);
    }

    // ── UiChangeModeAttribute ─────────────────────────────────────────────────

    [Fact]
    public void UiChangeModeAttribute_SectionDefault_Immediate()
    {
        var page = UiMetadataReader.ReadPage<IAppearanceUiSettings>();
        Assert.Equal(UiChangeMode.Immediate, page.DefaultChangeMode);
    }

    [Fact]
    public void UiChangeModeAttribute_SectionDefault_OnConfirm()
    {
        var page = UiMetadataReader.ReadPage<INetworkUiSettings>();
        Assert.Equal(UiChangeMode.OnConfirm, page.DefaultChangeMode);
    }

    [Fact]
    public void UiChangeModeAttribute_PropertyInheritsFromSection()
    {
        var page = UiMetadataReader.ReadPage<INetworkUiSettings>();
        var propMeta = page.Properties.First(p => p.PropertyName == nameof(INetworkUiSettings.UseProxy));
        Assert.Equal(UiChangeMode.OnConfirm, propMeta.ChangeMode);
    }

    // ── UiControlAttribute ────────────────────────────────────────────────────

    [Fact]
    public void UiControlAttribute_DropDown()
    {
        var page = UiMetadataReader.ReadPage<IAppearanceUiSettings>();
        var accentMeta = page.Properties.First(p => p.PropertyName == nameof(IAppearanceUiSettings.AccentColor));
        Assert.Equal(UiControlType.DropDown, accentMeta.ControlType);
    }

    [Fact]
    public void UiControlAttribute_UpDown_WithRange()
    {
        var page = UiMetadataReader.ReadPage<IAppearanceUiSettings>();
        var fontMeta = page.Properties.First(p => p.PropertyName == nameof(IAppearanceUiSettings.FontSize));
        Assert.Equal(UiControlType.UpDown, fontMeta.ControlType);
        Assert.Equal(8.0, fontMeta.ControlAttribute!.Minimum);
        Assert.Equal(72.0, fontMeta.ControlAttribute.Maximum);
        Assert.Equal(2.0, fontMeta.ControlAttribute.Increment);
    }

    [Fact]
    public void UiControlAttribute_Slider()
    {
        var page = UiMetadataReader.ReadPage<IVolumeUiSettings>();
        var volumeMeta = page.Properties.First(p => p.PropertyName == nameof(IVolumeUiSettings.Volume));
        Assert.Equal(UiControlType.Slider, volumeMeta.ControlType);
        Assert.Equal(0.0, volumeMeta.ControlAttribute!.Minimum);
        Assert.Equal(100.0, volumeMeta.ControlAttribute.Maximum);
    }

    [Fact]
    public void UiControlAttribute_RadioButton()
    {
        var page = UiMetadataReader.ReadPage<IVolumeUiSettings>();
        var modeMeta = page.Properties.First(p => p.PropertyName == nameof(IVolumeUiSettings.OutputMode));
        Assert.Equal(UiControlType.RadioButton, modeMeta.ControlType);
    }

    // ── Control type inference ────────────────────────────────────────────────

    [Fact]
    public void InferControlType_Bool_ReturnsCheckBox()
    {
        var page = UiMetadataReader.ReadPage<IAppearanceUiSettings>();
        var darkMeta = page.Properties.First(p => p.PropertyName == nameof(IAppearanceUiSettings.DarkMode));
        Assert.Equal(UiControlType.CheckBox, darkMeta.ControlType);
    }

    [Fact]
    public void InferControlType_String_ReturnsTextBox()
    {
        Assert.Equal(UiControlType.TextBox, UiMetadataReader.InferControlType(typeof(string)));
    }

    [Fact]
    public void InferControlType_Int_ReturnsUpDown()
    {
        Assert.Equal(UiControlType.UpDown, UiMetadataReader.InferControlType(typeof(int)));
    }

    [Fact]
    public void InferControlType_Enum_ReturnsDropDown()
    {
        Assert.Equal(UiControlType.DropDown, UiMetadataReader.InferControlType(typeof(UiControlType)));
    }

    [Fact]
    public void InferControlType_NullableBool_ReturnsCheckBox()
    {
        Assert.Equal(UiControlType.CheckBox, UiMetadataReader.InferControlType(typeof(bool?)));
    }

    // ── UiGroupAttribute ──────────────────────────────────────────────────────

    [Fact]
    public void UiGroupAttribute_AssociatesProperties()
    {
        var page = UiMetadataReader.ReadPage<IAppearanceUiSettings>();
        var accentMeta = page.Properties.First(p => p.PropertyName == nameof(IAppearanceUiSettings.AccentColor));
        var fontMeta = page.Properties.First(p => p.PropertyName == nameof(IAppearanceUiSettings.FontSize));
        Assert.Equal("Theme", accentMeta.GroupName);
        Assert.Equal("Theme", fontMeta.GroupName);
    }

    [Fact]
    public void UiGroupAttribute_GroupOrder()
    {
        var page = UiMetadataReader.ReadPage<INetworkUiSettings>();
        var hostMeta = page.Properties.First(p => p.PropertyName == nameof(INetworkUiSettings.ProxyHost));
        Assert.Equal(5, hostMeta.Order);
    }

    // ── UiLabelKeyAttribute ───────────────────────────────────────────────────

    [Fact]
    public void UiLabelKeyAttribute_LabelKey()
    {
        var page = UiMetadataReader.ReadPage<IAppearanceUiSettings>();
        var darkMeta = page.Properties.First(p => p.PropertyName == nameof(IAppearanceUiSettings.DarkMode));
        Assert.Equal("ui_dark_mode_label", darkMeta.LabelKey);
    }

    [Fact]
    public void UiLabelKeyAttribute_DescriptionKey()
    {
        var page = UiMetadataReader.ReadPage<IAppearanceUiSettings>();
        var darkMeta = page.Properties.First(p => p.PropertyName == nameof(IAppearanceUiSettings.DarkMode));
        Assert.Equal("ui_dark_mode_tooltip", darkMeta.DescriptionKey);
    }

    // ── UiConditionalVisibilityAttribute ─────────────────────────────────────

    [Fact]
    public void UiConditionalVisibilityAttribute_ConditionProperty()
    {
        var page = UiMetadataReader.ReadPage<INetworkUiSettings>();
        var hostMeta = page.Properties.First(p => p.PropertyName == nameof(INetworkUiSettings.ProxyHost));
        Assert.Equal(nameof(INetworkUiSettings.UseProxy), hostMeta.VisibilityConditionProperty);
        Assert.False(hostMeta.InvertVisibility);
    }

    // ── UiConditionalEnableAttribute ──────────────────────────────────────────

    [Fact]
    public void UiConditionalEnableAttribute_ConditionProperty()
    {
        var page = UiMetadataReader.ReadPage<IAppearanceUiSettings>();
        var fontMeta = page.Properties.First(p => p.PropertyName == nameof(IAppearanceUiSettings.FontSize));
        Assert.Equal(nameof(IAppearanceUiSettings.DarkMode), fontMeta.EnableConditionProperty);
        Assert.True(fontMeta.InvertEnable);
    }

    [Fact]
    public void UiConditionalEnableAttribute_ProxyPort_EnabledByUseProxy()
    {
        var page = UiMetadataReader.ReadPage<INetworkUiSettings>();
        var portMeta = page.Properties.First(p => p.PropertyName == nameof(INetworkUiSettings.ProxyPort));
        Assert.Equal(nameof(INetworkUiSettings.UseProxy), portMeta.EnableConditionProperty);
        Assert.False(portMeta.InvertEnable);
    }

    // ── Properties sorted by Order ────────────────────────────────────────────

    [Fact]
    public void Properties_AreSortedByOrder()
    {
        var page = UiMetadataReader.ReadPage<IAppearanceUiSettings>();
        var orders = page.Properties.Select(p => p.Order).ToList();
        var sorted = orders.OrderBy(x => x).ToList();
        Assert.Equal(sorted, orders);
    }
}
