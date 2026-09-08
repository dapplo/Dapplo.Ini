// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using Dapplo.Ini.Internationalization.Configuration;

namespace Dapplo.Ini.Internationalization.Tests;

public sealed class NotifyPropertyChangedLanguageTests
{
    private static readonly string LangDir =
        Path.Combine(AppContext.BaseDirectory, "Lang");

    [Fact]
    public void InterfaceWithoutINPC_GeneratedClass_DoesNotImplementINPC()
    {
        var section = new MainLanguageImpl();
        Assert.False((object)section is INotifyPropertyChanged,
            "Generated class for non-INPC language interface must not implement INPC.");
        Assert.False((object)section is INotifyPropertyChanging,
            "Generated class for non-INPC language interface must not implement INotifyPropertyChanging.");
    }

    [Fact]
    public void InterfaceWithINPC_GeneratedClass_ImplementsINPC()
    {
        var section = new NotifyMainLanguageImpl();
        Assert.True(section is INotifyPropertyChanged,
            "Generated class for INPC language interface must implement INPC.");
    }

    [Fact]
    public void SetLanguage_RaisesPropertyChanged_OnlyForChangedProperties()
    {
        var section = new NotifyMainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<INotifyMainLanguage>(section)
            .Build();

        Assert.Equal("Welcome to the application!", section.WelcomeMessage);
        Assert.Equal("Error", section.ErrorTitle);
        Assert.Equal("Save", section.SaveButton);
        Assert.Equal("Cancel", section.CancelButton);

        var changedProperties = new List<string>();
        var valuesDuringChange = new Dictionary<string, string>();

        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
            {
                changedProperties.Add(e.PropertyName);
                if (e.PropertyName == nameof(INotifyMainLanguage.WelcomeMessage))
                    valuesDuringChange[nameof(INotifyMainLanguage.WelcomeMessage)] = section.WelcomeMessage;
                if (e.PropertyName == nameof(INotifyMainLanguage.SaveButton))
                    valuesDuringChange[nameof(INotifyMainLanguage.SaveButton)] = section.SaveButton;
            }
        };

        // Switch to de-DE:
        // WelcomeMessage: "Welcome to the application!" -> "Willkommen bei der Anwendung!" (changed)
        // ErrorTitle: "Error" -> "Fehler" (changed)
        // SaveButton: "Save" -> "Speichern" (changed)
        // CancelButton: missing in de-DE -> falls back to "Cancel" (UNCHANGED!)
        // MultiLine, TabValue, BackslashValue: missing in de-DE -> UNCHANGED!
        config.SetLanguage("de-DE");

        Assert.Contains(nameof(INotifyMainLanguage.WelcomeMessage), changedProperties);
        Assert.Contains(nameof(INotifyMainLanguage.ErrorTitle), changedProperties);
        Assert.Contains(nameof(INotifyMainLanguage.SaveButton), changedProperties);
        Assert.Contains("Item[]", changedProperties);

        // CancelButton and other unchanged properties must NOT have fired PropertyChanged!
        Assert.DoesNotContain(nameof(INotifyMainLanguage.CancelButton), changedProperties);
        Assert.DoesNotContain(nameof(INotifyMainLanguage.MultiLine), changedProperties);
        Assert.DoesNotContain(nameof(INotifyMainLanguage.TabValue), changedProperties);
        Assert.DoesNotContain(nameof(INotifyMainLanguage.BackslashValue), changedProperties);

        // Verify values read during event handler return the new values
        Assert.Equal("Willkommen bei der Anwendung!", valuesDuringChange[nameof(INotifyMainLanguage.WelcomeMessage)]);
        Assert.Equal("Speichern", valuesDuringChange[nameof(INotifyMainLanguage.SaveButton)]);
    }

    [Fact]
    public void SetLanguage_SameLanguage_DoesNotRaisePropertyChanged()
    {
        var section = new NotifyMainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<INotifyMainLanguage>(section)
            .Build();

        var changedProperties = new List<string>();
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
        };

        // Reload the same language -> translations are identical, nothing changed
        config.SetLanguage("en-US");

        Assert.Empty(changedProperties);
    }

    [Fact]
    public async Task SetLanguageAsync_RaisesPropertyChanged_OnlyForChangedProperties()
    {
        var section = new NotifyMainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<INotifyMainLanguage>(section)
            .Build();

        var changedProperties = new List<string>();
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
        };

        await config.SetLanguageAsync("de-DE");

        Assert.Contains(nameof(INotifyMainLanguage.WelcomeMessage), changedProperties);
        Assert.Contains(nameof(INotifyMainLanguage.ErrorTitle), changedProperties);
        Assert.Contains(nameof(INotifyMainLanguage.SaveButton), changedProperties);
        Assert.DoesNotContain(nameof(INotifyMainLanguage.CancelButton), changedProperties);
    }

    [Fact]
    public void PropertyChanging_FiresBeforePropertyChanged_WithOldValue()
    {
        var section = new NpcBothLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<INpcBothLanguage>(section)
            .Build();

        var changingProperties = new List<string>();
        var valueDuringChanging = "";

        section.PropertyChanging += (_, e) =>
        {
            if (e.PropertyName != null)
            {
                changingProperties.Add(e.PropertyName);
                if (e.PropertyName == nameof(INpcBothLanguage.WelcomeMessage))
                {
                    valueDuringChanging = section.WelcomeMessage;
                }
            }
        };

        config.SetLanguage("de-DE");

        Assert.Contains(nameof(INpcBothLanguage.WelcomeMessage), changingProperties);
        // During PropertyChanging, the value must still be the old (en-US) value
        Assert.Equal("Welcome to the application!", valueDuringChanging);
    }

    [Fact]
    public void SuppressPropertyChanged_And_SuppressPropertyChanging_AreRespected()
    {
        var section = new NpcBothLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<INpcBothLanguage>(section)
            .Build();

        var changingProperties = new List<string>();
        var changedProperties = new List<string>();

        section.PropertyChanging += (_, e) =>
        {
            if (e.PropertyName != null) changingProperties.Add(e.PropertyName);
        };
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
        };

        config.SetLanguage("de-DE");

        // WelcomeMessage: fires both
        Assert.Contains(nameof(INpcBothLanguage.WelcomeMessage), changingProperties);
        Assert.Contains(nameof(INpcBothLanguage.WelcomeMessage), changedProperties);

        // ErrorTitle: SuppressPropertyChanged = true -> fires changing, but NOT changed
        Assert.Contains(nameof(INpcBothLanguage.ErrorTitle), changingProperties);
        Assert.DoesNotContain(nameof(INpcBothLanguage.ErrorTitle), changedProperties);

        // SaveButton: SuppressPropertyChanging = true -> does NOT fire changing, but DOES fire changed
        Assert.DoesNotContain(nameof(INpcBothLanguage.SaveButton), changingProperties);
        Assert.Contains(nameof(INpcBothLanguage.SaveButton), changedProperties);
    }

    [Fact]
    public void SetTranslation_RaisesPropertyChanged_ForAffectedProperty()
    {
        var section = new NotifyMainLanguageImpl();
        var changedProperties = new List<string>();

        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
        };

        // Before setting, WelcomeMessage returns sentinel
        Assert.Equal("###WelcomeMessage###", section.WelcomeMessage);

        section.SetTranslation("welcomemessage", "Custom Welcome");

        Assert.Equal("Custom Welcome", section.WelcomeMessage);
        Assert.Contains(nameof(INotifyMainLanguage.WelcomeMessage), changedProperties);
        Assert.Contains("Item[]", changedProperties);
        Assert.DoesNotContain(nameof(INotifyMainLanguage.ErrorTitle), changedProperties);
    }

    [Fact]
    public void ClearTranslations_RaisesPropertyChanged_ForPreviouslySetProperty()
    {
        var section = new NotifyMainLanguageImpl();
        section.SetTranslation("welcomemessage", "Hello");

        var changedProperties = new List<string>();
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
        };

        section.ClearTranslations();

        Assert.Equal("###WelcomeMessage###", section.WelcomeMessage);
        Assert.Contains(nameof(INotifyMainLanguage.WelcomeMessage), changedProperties);
        Assert.Contains("Item[]", changedProperties);
    }

    [Fact]
    public void UpdateTranslations_IdenticalDictionary_DoesNotRaisePropertyChanged()
    {
        var section = new NotifyMainLanguageImpl();
        section.SetTranslation("welcomemessage", "Hello");

        var changedProperties = new List<string>();
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
        };

        // Update with identical dictionary
        section.UpdateTranslations(new Dictionary<string, string> { ["welcomemessage"] = "Hello" });

        Assert.Empty(changedProperties);
    }
}

