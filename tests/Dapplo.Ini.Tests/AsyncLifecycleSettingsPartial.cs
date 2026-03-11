// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

/// <summary>
/// Consumer-supplied partial class that implements the async lifecycle hook methods
/// for <see cref="IAsyncLifecycleSettings"/> using the non-generic async pattern.
/// </summary>
public partial class AsyncLifecycleSettingsImpl
{
    // ── Tracking flags (test helpers) ─────────────────────────────────────────

    public bool AfterLoadAsyncCalled  { get; set; }
    public bool BeforeSaveAsyncCalled { get; set; }
    public bool AfterSaveAsyncCalled  { get; set; }

    // ── IAfterLoadAsync ───────────────────────────────────────────────────────

    public Task OnAfterLoadAsync(CancellationToken cancellationToken = default)
    {
        AfterLoadAsyncCalled = true;
        return Task.CompletedTask;
    }

    // ── IBeforeSaveAsync ──────────────────────────────────────────────────────

    public Task<bool> OnBeforeSaveAsync(CancellationToken cancellationToken = default)
    {
        BeforeSaveAsyncCalled = true;
        return Task.FromResult(true);
    }

    // ── IAfterSaveAsync ───────────────────────────────────────────────────────

    public Task OnAfterSaveAsync(CancellationToken cancellationToken = default)
    {
        AfterSaveAsyncCalled = true;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Consumer-supplied partial class that implements the async before-save hook for
/// <see cref="IAsyncCancelSaveSettings"/> — always cancels the save.
/// </summary>
public partial class AsyncCancelSaveSettingsImpl
{
    public Task<bool> OnBeforeSaveAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
