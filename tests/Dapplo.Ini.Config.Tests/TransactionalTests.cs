// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Config.Configuration;

namespace Dapplo.Ini.Config.Tests;

public sealed class TransactionalTests
{
    [Fact]
    public void WithoutTransaction_SetPropagatesImmediately()
    {
        var section = new UserSettingsImpl();
        section.ResetToDefaults();

        section.Username = "alice";
        Assert.Equal("alice", section.Username);
    }

    [Fact]
    public void Begin_Then_Rollback_RestoresOriginalValue()
    {
        var section = new UserSettingsImpl();
        section.ResetToDefaults();
        section.Username = "alice";

        section.Begin();
        Assert.True(section.IsInTransaction);

        section.Username = "bob"; // pending

        section.Rollback();
        Assert.False(section.IsInTransaction);

        // After rollback the get still returns the pre-transaction value
        Assert.Equal("alice", section.Username);
    }

    [Fact]
    public void Begin_Then_Commit_MakesNewValueVisible()
    {
        var section = new UserSettingsImpl();
        section.ResetToDefaults();
        section.Username = "alice";

        section.Begin();
        section.Username = "bob";
        section.Commit();

        Assert.False(section.IsInTransaction);
        Assert.Equal("bob", section.Username);
    }

    [Fact]
    public void Begin_WhenAlreadyInTransaction_IsNoOp()
    {
        var section = new UserSettingsImpl();
        section.ResetToDefaults();
        section.Begin();
        section.Username = "charlie";
        section.Begin(); // second Begin should not wipe the pending value
        section.Commit();
        Assert.Equal("charlie", section.Username);
    }

    [Fact]
    public void Commit_WhenNotInTransaction_IsNoOp()
    {
        var section = new UserSettingsImpl();
        section.ResetToDefaults();
        section.Username = "alice";
        section.Commit(); // no-op
        Assert.Equal("alice", section.Username);
    }
}
