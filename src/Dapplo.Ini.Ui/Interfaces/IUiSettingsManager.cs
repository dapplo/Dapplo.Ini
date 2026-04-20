// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Ui.Interfaces;

/// <summary>
/// Provides UI-specific lifecycle operations for a settings session: applying pending
/// changes to the underlying configuration, or rolling them back.
/// </summary>
/// <remarks>
/// <para>
/// A <em>settings session</em> is the period while the settings window is open.
/// During this time the UI framework buffers changes and provides a consistent snapshot
/// to the user.  When the user confirms (OK / Apply), <see cref="Apply"/> is called;
/// when the user cancels or closes without confirming, <see cref="Rollback"/> is called.
/// </para>
/// <para>
/// This interface is typically implemented by a framework-provided settings-manager class
/// that wraps one or more <c>IIniSection</c> instances.  Consumers can inject it via DI
/// and call it from their own confirm/cancel button handlers.
/// </para>
/// </remarks>
public interface IUiSettingsManager
{
    /// <summary>
    /// Gets a value indicating whether the user has made any changes since the session
    /// was started or since the last <see cref="Apply"/> call.
    /// </summary>
    bool HasPendingChanges { get; }

    /// <summary>
    /// Applies all pending changes to the underlying configuration sections.
    /// After this call <see cref="HasPendingChanges"/> returns <c>false</c>.
    /// </summary>
    void Apply();

    /// <summary>
    /// Discards all pending changes and restores the configuration sections to the values
    /// they had when the session started (or when <see cref="Apply"/> was last called).
    /// After this call <see cref="HasPendingChanges"/> returns <c>false</c>.
    /// </summary>
    void Rollback();

    /// <summary>
    /// Starts a new settings session by recording the current values of all registered
    /// sections as the rollback point.  Call this when the settings window opens.
    /// </summary>
    void BeginSession();

    /// <summary>
    /// Ends the settings session.  If <see cref="HasPendingChanges"/> is <c>true</c>
    /// this method rolls back the pending changes automatically.
    /// </summary>
    void EndSession();
}
