// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Parsing;

namespace Dapplo.Ini.Tests;

/// <summary>A section with the usual property kinds, for the allocation tests.</summary>
public interface IAllocationSettings : IIniSection
{
    string? Name { get; set; }
    int Count { get; set; }
    bool Enabled { get; set; }
    double Ratio { get; set; }
    Dictionary<string, int>? Limits { get; set; }
}

/// <summary>
/// Reading and applying configuration values avoids heap allocations where possible: values are parsed from spans,
/// generated sections match keys without lower-casing them, typed converters avoid boxing.
/// </summary>
public sealed class LowAllocationParsingTests
{
    private static long AllocatedBy(Action action)
    {
        for (var i = 0; i < 5; i++) action();   // warm up (JIT, static initialisation, converter caches)
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10; i++) action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void ApplyingValuesToAGeneratedSection_DoesNotAllocate()
    {
        var section = new AllocationSettingsImpl();
        var entries = new[] { ("name", "Robin"), ("COUNT", "42"), ("Enabled", "yes"), ("Ratio", "1.5") };

        var allocated = AllocatedBy(() =>
        {
            foreach (var (key, value) in entries)
            {
                section.SetRawValue(key, value);
                Assert.True(section.IsKnownKey(key));
            }
        });

        Assert.Equal(0, allocated);
        Assert.Equal("Robin", section.Name);
        Assert.Equal(42, section.Count);
        Assert.True(section.Enabled);
        Assert.Equal(1.5, section.Ratio);
    }

    [Fact]
    public void GeneratedKeyMatching_IsCaseInsensitive_IncludingSubKeyDictionaries()
    {
        var section = new AllocationSettingsImpl();

        section.SetRawValue("LIMITS.Upload", "10");
        section.SetRawValue("limits.download", "20");

        Assert.Equal(10, section.Limits!["upload"]);
        Assert.Equal(20, section.Limits["Download"]);
        Assert.True(section.IsKnownKey("Limits.Anything"));
        Assert.False(section.IsKnownKey("Limits"));      // the dictionary itself is written as sub-keys only
        Assert.False(section.IsKnownKey("Unknown"));
        Assert.Equal(typeof(int), section.GetPropertyType("count"));
        Assert.Equal(typeof(Dictionary<string, int>), section.GetPropertyType("limits.x"));
        Assert.Equal(42, new AllocationSettingsImpl { Count = 42 }.GetValue<int>("COUNT"));
    }

    [Fact]
    public void Parse_AllocatesLittleMoreThanTheResult()
    {
        var text = "[Main]\n" + string.Join("\n", Enumerable.Range(0, 200).Select(i => $"Key{i} = Value {i}"));

        var allocated = AllocatedBy(() => IniFileParser.Parse(text)) / 10;

        // The result: 200 keys (~40 B) + 200 values (~44 B) + 200 entries (~48 B) + a right-sized dictionary and
        // list (~10 KB) + the section ≈ 37 KB. Growing collections or intermediate strings would add a lot more.
        Assert.True(allocated < 45_000, $"Parse allocated {allocated} bytes");
    }

    [Theory]
    [InlineData("\"quoted\"", false, false, "\"quoted\"")]
    [InlineData("\"quoted\"", true, false, "quoted")]
    [InlineData("'single'", true, false, "single")]
    [InlineData("\"C:\\Temp\\\\\"", true, false, "C:\\Temp\\")]      // the writer doubles a trailing backslash
    [InlineData("\"say \\\"hi\\\"\"", true, false, "say \"hi\"")]
    [InlineData("\"a\\tb\\x41\\n\"", true, true, "a\tbA\n")]
    [InlineData("a\\tb", false, true, "a\tb")]
    [InlineData("unknown \\q escape", false, true, "unknown \\q escape")]
    [InlineData("\"", true, true, "\"")]
    public void Values_QuotesAndEscapes(string raw, bool quoted, bool escapes, string expected)
    {
        var options = new IniParserOptions { QuotedValues = quoted, EscapeSequences = escapes };
        var ini = IniFileParser.Parse($"[S]\nKey = {raw}\n", options);
        Assert.Equal(expected, ini.GetSection("S")!.GetValue("Key"));
    }

    [Fact]
    public void LongValues_UseThePooledPath()
    {
        var longText = string.Concat(Enumerable.Repeat("abc\\n", 500));   // 2,500 chars
        var options = new IniParserOptions { QuotedValues = true, EscapeSequences = true };

        var ini = IniFileParser.Parse($"[S]\nKey = \"{longText}\"\n", options);

        Assert.Equal(string.Concat(Enumerable.Repeat("abc\n", 500)), ini.GetSection("S")!.GetValue("Key"));
    }

    [Fact]
    public void LineContinuation_StillJoinsLines()
    {
        var options = new IniParserOptions { LineContinuation = true, QuotedValues = true };
        var ini = IniFileParser.Parse("[S]\nKey = \"first \\\n  second\"\nNext = 1\n", options);
        Assert.Equal("first second", ini.GetSection("S")!.GetValue("Key"));
        Assert.Equal("1", ini.GetSection("S")!.GetValue("Next"));
    }

    [Fact]
    public void SectionSizes_PrePass_HandlesUnclosedHeadersAndRepeatedSections()
    {
        var ini = IniFileParser.Parse("a=1\n[Unclosed\nb=2\n[S]\nc=3\n[T]\nd=4\n[S]\ne=5\n");
        Assert.Equal(new[] { "a", "b" }, ini.GetSection("")!.Entries.Select(e => e.Key));
        Assert.Equal(new[] { "c", "e" }, ini.GetSection("S")!.Entries.Select(e => e.Key));
        Assert.Equal(new[] { "d" }, ini.GetSection("T")!.Entries.Select(e => e.Key));
    }
}
