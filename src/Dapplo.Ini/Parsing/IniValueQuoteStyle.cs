// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Parsing;

/// <summary>
/// Controls how values are quoted when writing an INI file.
/// </summary>
public enum IniValueQuoteStyle
{
    /// <summary>Do not override; inherit from file-level writer options.</summary>
    Default = 0,
    /// <summary>Never quote values.</summary>
    Never = 1,
    /// <summary>Always quote values using single quotes.</summary>
    Single = 2,
    /// <summary>Always quote values using double quotes.</summary>
    Double = 3,
    /// <summary>Quote only when needed (e.g. leading/trailing whitespace, comment prefix, or assignment delimiter).</summary>
    Auto = 4
}
