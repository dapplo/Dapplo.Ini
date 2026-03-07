// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Config.Interfaces;

/// <summary>
/// Hook called on an INI section immediately after its values have been loaded from the INI file.
/// </summary>
public interface IAfterLoad
{
    /// <summary>Called after the section's values have been populated from the INI file.</summary>
    void OnAfterLoad();
}
