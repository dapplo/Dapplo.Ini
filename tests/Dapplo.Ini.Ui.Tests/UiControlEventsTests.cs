// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Ui.Interfaces;

namespace Dapplo.Ini.Ui.Tests;

/// <summary>
/// Tests for <see cref="IUiControlEvents"/> default interface methods.
/// </summary>
public sealed class UiControlEventsTests
{
    /// <summary>
    /// Verifies that the default implementations of <see cref="IUiControlEvents"/> are
    /// no-ops and can be called without throwing.
    /// </summary>
    [Fact]
    public void DefaultImplementations_DoNotThrow()
    {
        IUiControlEvents handler = new DefaultEventsHandler();

        var ex1 = Record.Exception(() => handler.OnPropertyChanged("Prop", null, "new"));
        var ex2 = Record.Exception(() => handler.OnControlFocused("Prop"));
        var ex3 = Record.Exception(() => handler.OnControlBlurred("Prop"));
        var ex4 = Record.Exception(() => handler.OnControlClicked("Prop", true));

        Assert.Null(ex1);
        Assert.Null(ex2);
        Assert.Null(ex3);
        Assert.Null(ex4);
    }

    [Fact]
    public void CustomImplementation_ReceivesCallbacks()
    {
        var handler = new TrackingEventsHandler();

        handler.OnPropertyChanged("Port", 80, 8080);
        handler.OnControlFocused("Host");
        handler.OnControlBlurred("Host");
        handler.OnControlClicked("Enabled", true);

        Assert.Equal(("Port", (object?)80, (object?)8080), handler.LastChanged);
        Assert.Equal("Host", handler.LastFocused);
        Assert.Equal("Host", handler.LastBlurred);
        Assert.Equal(("Enabled", (object?)true), handler.LastClicked);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>A handler that uses all default implementations.</summary>
    private sealed class DefaultEventsHandler : IUiControlEvents { }

    /// <summary>A handler that records what it receives.</summary>
    private sealed class TrackingEventsHandler : IUiControlEvents
    {
        public (string, object?, object?)? LastChanged { get; private set; }
        public string? LastFocused { get; private set; }
        public string? LastBlurred { get; private set; }
        public (string, object?)? LastClicked { get; private set; }

        public void OnPropertyChanged(string propertyName, object? oldValue, object? newValue)
            => LastChanged = (propertyName, oldValue, newValue);

        public void OnControlFocused(string propertyName)
            => LastFocused = propertyName;

        public void OnControlBlurred(string propertyName)
            => LastBlurred = propertyName;

        public void OnControlClicked(string propertyName, object? newValue)
            => LastClicked = (propertyName, newValue);
    }
}
