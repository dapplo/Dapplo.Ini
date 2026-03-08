// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// This file demonstrates the legacy (non-generic) lifecycle hook pattern.
// The consumer provides implementations of IAfterLoad, IBeforeSave, and IAfterSave
// in a partial class alongside the source-generated class.
// For new code, prefer the generic IAfterLoad<TSelf> / IBeforeSave<TSelf> / IAfterSave<TSelf>
// pattern, which keeps the implementation inside the interface itself.

namespace Dapplo.IniConfig.Tests;

/// <summary>
/// Consumer-supplied partial class that implements the lifecycle hook methods
/// for <see cref="ILegacyLifecycleSettings"/> using the old non-generic pattern.
/// The generated part lives in LegacyLifecycleSettingsImpl.g.cs (produced by the source generator).
/// </summary>
public partial class LegacyLifecycleSettingsImpl
{
    // ── Tracking flags (test helpers) ─────────────────────────────────────────

    public bool AfterLoadCalled  { get; private set; }
    public bool BeforeSaveCalled { get; private set; }
    public bool AfterSaveCalled  { get; private set; }

    // ── IAfterLoad ────────────────────────────────────────────────────────────

    public void OnAfterLoad() => AfterLoadCalled = true;

    // ── IBeforeSave ───────────────────────────────────────────────────────────

    public bool OnBeforeSave() { BeforeSaveCalled = true; return true; }

    // ── IAfterSave ────────────────────────────────────────────────────────────

    public void OnAfterSave() => AfterSaveCalled = true;
}
