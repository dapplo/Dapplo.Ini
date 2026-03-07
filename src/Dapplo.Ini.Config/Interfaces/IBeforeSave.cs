// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Config.Interfaces;

/// <summary>
/// Hook called on an INI section immediately <em>before</em> the INI file is written to disk.
/// Implement this on your section interface to perform pre-save logic (e.g. validation).
/// </summary>
public interface IBeforeSave
{
    /// <summary>
    /// Called before the INI file is saved. Return <c>false</c> to abort the save operation.
    /// </summary>
    bool OnBeforeSave();
}
