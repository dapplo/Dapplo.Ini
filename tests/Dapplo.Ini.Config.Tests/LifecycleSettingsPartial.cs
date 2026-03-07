// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// This file provides the consumer-side implementations of lifecycle hooks
// for the generated LifecycleSettingsImpl class (IBeforeSave, IAfterSave, IAfterLoad).
// In a real application this would live alongside other application code, not in a test file.

namespace Dapplo.Ini.Config.Tests;

/// <summary>
/// Consumer-supplied partial class that implements the lifecycle hook methods
/// for <see cref="ILifecycleSettings"/>.
/// The generated part lives in LifecycleSettingsImpl.g.cs (produced by the source generator).
/// </summary>
public partial class LifecycleSettingsImpl
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
