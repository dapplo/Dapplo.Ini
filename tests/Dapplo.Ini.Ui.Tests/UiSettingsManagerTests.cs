// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Attributes;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Ui.Configuration;

namespace Dapplo.Ini.Ui.Tests;

// ── Sample section for manager tests ─────────────────────────────────────────

[IniSection("ManagerTest")]
public interface IManagerTestSettings : IIniSection, ITransactional
{
    [IniValue(DefaultValue = "8080", Transactional = true)]
    int Port { get; set; }

    [IniValue(DefaultValue = "localhost", Transactional = true)]
    string? Host { get; set; }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Tests for <see cref="UiSettingsManager"/>.
/// </summary>
public sealed class UiSettingsManagerTests
{
    [Fact]
    public void RegisterSection_Null_Throws()
    {
        var mgr = new UiSettingsManager();
        Assert.Throws<ArgumentNullException>(() => mgr.RegisterSection(null!));
    }

    [Fact]
    public void BeginSession_Then_Apply_CommitsChanges()
    {
        var section = new ManagerTestSettingsImpl();
        section.ResetToDefaults();

        var mgr = new UiSettingsManager();
        mgr.RegisterSection(section);

        mgr.BeginSession();
        Assert.True(section.IsInTransaction);

        section.Port = 9090;
        mgr.Apply();

        Assert.False(section.IsInTransaction);
        Assert.Equal(9090, section.Port);
    }

    [Fact]
    public void BeginSession_Then_Rollback_RestoresValues()
    {
        var section = new ManagerTestSettingsImpl();
        section.ResetToDefaults();
        section.Port = 1234;

        var mgr = new UiSettingsManager();
        mgr.RegisterSection(section);

        mgr.BeginSession();
        section.Port = 9999;
        mgr.Rollback();

        Assert.False(section.IsInTransaction);
        Assert.Equal(1234, section.Port);
    }

    [Fact]
    public void EndSession_WithPendingChanges_AutoRollsBack()
    {
        var section = new ManagerTestSettingsImpl();
        section.ResetToDefaults();
        section.Host = "original";

        var mgr = new UiSettingsManager();
        mgr.RegisterSection(section);

        mgr.BeginSession();
        section.Host = "changed";

        // End session without Apply → should auto-rollback
        mgr.EndSession();

        Assert.Equal("original", section.Host);
    }

    [Fact]
    public void HasPendingChanges_IsFalse_BeforeBeginSession()
    {
        var mgr = new UiSettingsManager();
        var section = new ManagerTestSettingsImpl();
        section.ResetToDefaults();
        mgr.RegisterSection(section);

        Assert.False(mgr.HasPendingChanges);
    }

    [Fact]
    public void RegisterSection_SameSection_NotAddedTwice()
    {
        var section = new ManagerTestSettingsImpl();
        section.ResetToDefaults();

        var mgr = new UiSettingsManager();
        mgr.RegisterSection(section);
        mgr.RegisterSection(section); // duplicate registration

        // Should not throw and should work correctly
        mgr.BeginSession();
        section.Port = 7777;
        mgr.Rollback();

        // Rollback should be applied only once (not double-rolled-back)
        Assert.False(section.IsInTransaction);
    }
}
