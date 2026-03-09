// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.IniConfig.Interfaces;

/// <summary>
/// The decision returned by a <see cref="FileChangedCallback"/> registered via
/// <see cref="Configuration.IniConfigBuilder.OnFileChanged"/>.
/// </summary>
public enum ReloadDecision
{
    /// <summary>Reload the configuration immediately.</summary>
    Reload,

    /// <summary>
    /// Ignore this particular file-change notification. The configuration will not be reloaded.
    /// </summary>
    Ignore,

    /// <summary>
    /// Postpone the reload.  The library will call
    /// <see cref="Configuration.IniConfig.Reload"/> at the next opportunity when the consumer
    /// signals readiness via <see cref="Configuration.IniConfig.RequestPostponedReload"/>.
    /// </summary>
    Postpone,
}

/// <summary>
/// A callback invoked by the file-change monitor when the watched INI file changes on disk.
/// </summary>
/// <param name="filePath">The absolute path of the file that changed.</param>
/// <returns>
/// A <see cref="ReloadDecision"/> that controls how the library reacts to the change.
/// </returns>
public delegate ReloadDecision FileChangedCallback(string filePath);
