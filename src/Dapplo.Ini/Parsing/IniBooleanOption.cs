// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Parsing;

/// <summary>
/// Three-state option used by attributes to inherit or override a boolean writer setting.
/// </summary>
public enum IniBooleanOption
{
    /// <summary>Do not override; inherit from file-level writer options.</summary>
    Default = 0,
    /// <summary>Enable the setting.</summary>
    Enabled = 1,
    /// <summary>Disable the setting.</summary>
    Disabled = 2
}
