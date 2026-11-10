// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.WindowsPolicies.Watching;

/// <summary>Reports changes below a policy root; disposing it stops the watching.</summary>
internal interface IPolicyChangeWatcher : IDisposable
{
    /// <summary>Raised (on a background thread) for every change notification; not debounced.</summary>
    event EventHandler? Changed;
}
