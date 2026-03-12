// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Tests;

/// <summary>
/// Partial implementation of <see cref="ILegacyMigrationSettings"/> that
/// implements <see cref="Interfaces.IUnknownKey"/> via the non-generic pattern.
/// </summary>
public partial class LegacyMigrationSettingsImpl : Interfaces.IUnknownKey
{
    /// <summary>Last unknown key received (used in tests).</summary>
    public string? LastUnknownKey { get; private set; }

    /// <summary>Last unknown value received (used in tests).</summary>
    public string? LastUnknownValue { get; private set; }

    public void OnUnknownKey(string key, string? value)
    {
        LastUnknownKey = key;
        LastUnknownValue = value;

        // Example migration: "OldValue" was renamed to "Value"
        if (key.Equals("OldValue", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value, out var parsed))
        {
            Value = parsed;
        }
    }
}
