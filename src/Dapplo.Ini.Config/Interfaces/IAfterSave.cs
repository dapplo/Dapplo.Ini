// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Config.Interfaces;

/// <summary>
/// Hook called on an INI section immediately <em>after</em> the INI file has been written to disk.
/// </summary>
public interface IAfterSave
{
    /// <summary>Called after the INI file has been saved successfully.</summary>
    void OnAfterSave();
}
