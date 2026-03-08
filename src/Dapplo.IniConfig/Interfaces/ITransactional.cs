// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.IniConfig.Interfaces;

/// <summary>
/// Allows an INI section to participate in a transaction.
/// While a transaction is active, <c>get</c> returns the committed (old) value;
/// only after <see cref="Commit"/> the new values become visible.
/// </summary>
public interface ITransactional
{
    /// <summary>Gets whether a transaction is currently active.</summary>
    bool IsInTransaction { get; }

    /// <summary>Starts a new transaction, recording the current values as the rollback point.</summary>
    void Begin();

    /// <summary>Commits the pending changes, making new values visible to readers.</summary>
    void Commit();

    /// <summary>Discards any pending changes and restores the values from before <see cref="Begin"/>.</summary>
    void Rollback();
}
