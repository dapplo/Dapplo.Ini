// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

global using Xunit;
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;

namespace Dapplo.IniConfig.Tests;

/// <summary>
/// Disables parallel execution for test classes that share the global
/// <see cref="Dapplo.IniConfig.Configuration.IniConfigRegistry"/> singleton.
/// Without this, a test class that takes several hundred milliseconds (e.g. async auto-save tests)
/// can race with another class whose constructor calls <c>IniConfigRegistry.Clear()</c>.
/// </summary>
[CollectionDefinition("IniConfigRegistry")]
public sealed class IniConfigRegistryCollection { }
