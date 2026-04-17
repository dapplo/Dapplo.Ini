// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Parsing;

/// <summary>
/// Controls how an <see cref="IniFile"/> is written.
/// </summary>
public sealed class IniWriterOptions
{
    /// <summary>
    /// The default writer options: spaces around <c>=</c>, no escaping, no quoting, and comments enabled.
    /// </summary>
    public static readonly IniWriterOptions Default = new IniWriterOptions();

    /// <summary>
    /// The separator string written between each key and value.
    /// </summary>
    public string AssignmentSeparator { get; set; } = " = ";

    /// <summary>
    /// Controls whether values are quoted when written.
    /// </summary>
    public IniValueQuoteStyle QuoteStyle { get; set; } = IniValueQuoteStyle.Never;

    /// <summary>
    /// When true, values are written with C-style escape sequences.
    /// </summary>
    public bool EscapeSequences { get; set; } = false;

    /// <summary>
    /// When true, comments are written; when false, section and key comments are omitted.
    /// </summary>
    public bool WriteComments { get; set; } = true;

    internal IniWriterOptions Clone() => new()
    {
        AssignmentSeparator = AssignmentSeparator,
        QuoteStyle = QuoteStyle,
        EscapeSequences = EscapeSequences,
        WriteComments = WriteComments
    };

    internal IniWriterOptions Apply(IniWriterOptionsOverride? optionsOverride)
    {
        if (optionsOverride == null || !optionsOverride.HasOverrides)
            return this;

        var copy = Clone();
        if (optionsOverride.AssignmentSeparator != null)
            copy.AssignmentSeparator = optionsOverride.AssignmentSeparator;
        if (optionsOverride.QuoteStyle != IniValueQuoteStyle.Default)
            copy.QuoteStyle = optionsOverride.QuoteStyle;
        switch (optionsOverride.EscapeSequences)
        {
            case IniBooleanOption.Enabled:
                copy.EscapeSequences = true;
                break;
            case IniBooleanOption.Disabled:
                copy.EscapeSequences = false;
                break;
        }
        switch (optionsOverride.WriteComments)
        {
            case IniBooleanOption.Enabled:
                copy.WriteComments = true;
                break;
            case IniBooleanOption.Disabled:
                copy.WriteComments = false;
                break;
        }
        return copy;
    }
}
