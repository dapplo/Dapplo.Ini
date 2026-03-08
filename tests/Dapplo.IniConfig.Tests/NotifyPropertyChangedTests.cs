// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;

namespace Dapplo.IniConfig.Tests;

public sealed class NotifyPropertyChangedTests
{
    [Fact]
    public void SettingProperty_WithNpcEnabled_RaisesPropertyChangedEvent()
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
    public void SettingProperty_WithNpcEnabled_RaisesPropertyChangingEvent()
    {
        var section = new GeneralSettingsImpl();
        section.ResetToDefaults();

        var changingNames = new List<string>();
        section.PropertyChanging += (_, e) =>
        {
            if (e.PropertyName != null) changingNames.Add(e.PropertyName);
        };

        section.AppName = "AnotherName";

        Assert.Contains("AppName", changingNames);
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
}
