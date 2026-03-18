// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Converters;

namespace Dapplo.Ini.Tests;

public sealed class ValueConverterTests
{
    [Theory]
    [InlineData("hello", "hello")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void StringConverter_RoundTrip(string? raw, string? expected)
    {
        var converter = new StringConverter();
        Assert.Equal(expected, converter.ConvertFromString(raw));
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("False", false)]
    [InlineData("true", true)]
    [InlineData(null, false)]
    public void BoolConverter_FromString(string? raw, bool expected)
    {
        var converter = new BoolConverter();
        Assert.Equal(expected, converter.ConvertFromString(raw));
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-1", -1)]
    [InlineData(null, 0)]
    public void Int32Converter_FromString(string? raw, int expected)
    {
        var converter = new Int32Converter();
        Assert.Equal(expected, converter.ConvertFromString(raw));
    }

    [Fact]
    public void Int32Converter_ToAndFromString_RoundTrip()
    {
        var converter = new Int32Converter();
        var raw = converter.ConvertToString(12345);
        Assert.Equal(12345, converter.ConvertFromString(raw));
    }

    [Fact]
    public void DoubleConverter_InvariantCulture_RoundTrip()
    {
        var converter = new DoubleConverter();
        var raw = converter.ConvertToString(3.14159);
        Assert.Equal(3.14159, converter.ConvertFromString(raw));
    }

    [Fact]
    public void DateTimeConverter_RoundTrip()
    {
        var dt = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var converter = new DateTimeConverter();
        var raw = converter.ConvertToString(dt);
        Assert.Equal(dt, converter.ConvertFromString(raw));
    }

    [Fact]
    public void TimeSpanConverter_RoundTrip()
    {
        var ts = TimeSpan.FromHours(2.5);
        var converter = new TimeSpanConverter();
        var raw = converter.ConvertToString(ts);
        Assert.Equal(ts, converter.ConvertFromString(raw));
    }

    [Fact]
    public void GuidConverter_RoundTrip()
    {
        var g = Guid.NewGuid();
        var converter = new GuidConverter();
        var raw = converter.ConvertToString(g);
        Assert.Equal(g, converter.ConvertFromString(raw));
    }

    [Fact]
    public void UriConverter_RoundTrip()
    {
        var uri = new Uri("https://example.com/path?q=1");
        var converter = new UriConverter();
        var raw = converter.ConvertToString(uri);
        Assert.Equal(uri, converter.ConvertFromString(raw));
    }

    public enum Color { Red, Green, Blue }

    [Fact]
    public void EnumConverter_FromString_Parses()
    {
        var converter = new EnumConverter(typeof(Color));
        Assert.Equal(Color.Green, converter.ConvertFromString("Green"));
        Assert.Equal(Color.Blue,  converter.ConvertFromString("blue")); // case-insensitive
    }

    [Fact]
    public void EnumConverter_ToAndFromString_RoundTrip()
    {
        var converter = new EnumConverter(typeof(Color));
        var raw = converter.ConvertToString(Color.Red);
        Assert.Equal(Color.Red, converter.ConvertFromString(raw));
    }

    [Fact]
    public void ValueConverterRegistry_GetConverter_ReturnsCorrectConverter()
    {
        Assert.IsType<Int32Converter>(ValueConverterRegistry.GetConverter(typeof(int)));
        Assert.IsType<BoolConverter>(ValueConverterRegistry.GetConverter(typeof(bool)));
        Assert.IsType<StringConverter>(ValueConverterRegistry.GetConverter(typeof(string)));
        Assert.IsType<DoubleConverter>(ValueConverterRegistry.GetConverter(typeof(double)));
    }

    [Fact]
    public void ValueConverterRegistry_GetConverter_HandlesNullable()
    {
        var conv = ValueConverterRegistry.GetConverter(typeof(int?));
        Assert.NotNull(conv);
        // Should be able to convert
        Assert.Equal(0, conv!.ConvertFromString(null));
    }

    [Fact]
    public void ValueConverterRegistry_GetConverter_HandlesEnum()
    {
        var conv = ValueConverterRegistry.GetConverter(typeof(Color));
        Assert.NotNull(conv);
    }

    [Fact]
    public void ValueConverterRegistry_CustomConverter_CanBeRegistered()
    {
        var custom = new StringConverter(); // reuse to test registration path
        ValueConverterRegistry.Register(custom);
        var fetched = ValueConverterRegistry.GetConverter(typeof(string));
        Assert.IsType<StringConverter>(fetched);
    }

    // ── ListConverter<T> ─────────────────────────────────────────────────────

    [Fact]
    public void ListConverter_FromString_ParsesCommaSeparated()
    {
        var converter = new ListConverter<string>(new StringConverter());
        var result = converter.ConvertFromString("alpha,beta,gamma");
        Assert.Equal(new List<string> { "alpha", "beta", "gamma" }, result);
    }

    [Fact]
    public void ListConverter_FromString_TrimsWhitespace()
    {
        var converter = new ListConverter<string>(new StringConverter());
        var result = converter.ConvertFromString(" a , b , c ");
        Assert.Equal(new List<string> { "a", "b", "c" }, result);
    }

    [Fact]
    public void ListConverter_FromString_EmptyString_ReturnsEmptyList()
    {
        var converter = new ListConverter<string>(new StringConverter());
        var result = converter.ConvertFromString("");
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public void ListConverter_FromString_Null_ReturnsDefault()
    {
        var converter = new ListConverter<string>(new StringConverter());
        Assert.Null(converter.ConvertFromString(null));
    }

    [Fact]
    public void ListConverter_ToAndFromString_RoundTrip()
    {
        var converter = new ListConverter<int>(new Int32Converter());
        var original = new List<int> { 1, 2, 3 };
        var raw = converter.ConvertToString(original);
        var result = converter.ConvertFromString(raw);
        Assert.Equal(original, result);
    }

    [Fact]
    public void ListConverter_ToStringFromObject_AcceptsIEnumerable()
    {
        IValueConverter converter = new ListConverter<string>(new StringConverter());
        IEnumerable<string> values = new[] { "p", "q" };
        var raw = converter.ConvertToString(values);
        Assert.Equal("p,q", raw);
    }

    // ── ArrayConverter<T> ────────────────────────────────────────────────────

    [Fact]
    public void ArrayConverter_FromString_ParsesCommaSeparated()
    {
        var converter = new ArrayConverter<string>(new StringConverter());
        var result = (string[]?)converter.ConvertFromString("x,y,z");
        Assert.Equal(new[] { "x", "y", "z" }, result);
    }

    [Fact]
    public void ArrayConverter_ToAndFromString_RoundTrip()
    {
        var converter = new ArrayConverter<int>(new Int32Converter());
        var original = new[] { 10, 20, 30 };
        var raw = converter.ConvertToString(original);
        var result = (int[]?)converter.ConvertFromString(raw);
        Assert.Equal(original, result);
    }

    // ── DictionaryConverter<TKey,TValue> ─────────────────────────────────────

    [Fact]
    public void DictionaryConverter_FromString_ParsesKeyValuePairs()
    {
        var converter = new DictionaryConverter<string, int>(new StringConverter(), new Int32Converter());
        var result = converter.ConvertFromString("a=1,b=2,c=3");
        Assert.Equal(3, result!.Count);
        Assert.Equal(1, result["a"]);
        Assert.Equal(2, result["b"]);
        Assert.Equal(3, result["c"]);
    }

    [Fact]
    public void DictionaryConverter_FromString_TrimsWhitespace()
    {
        var converter = new DictionaryConverter<string, int>(new StringConverter(), new Int32Converter());
        var result = converter.ConvertFromString(" x = 10 , y = 20 ");
        Assert.Equal(10, result!["x"]);
        Assert.Equal(20, result["y"]);
    }

    [Fact]
    public void DictionaryConverter_FromString_EmptyString_ReturnsEmptyDictionary()
    {
        var converter = new DictionaryConverter<string, int>(new StringConverter(), new Int32Converter());
        var result = converter.ConvertFromString("");
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public void DictionaryConverter_FromString_Null_ReturnsDefault()
    {
        var converter = new DictionaryConverter<string, int>(new StringConverter(), new Int32Converter());
        Assert.Null(converter.ConvertFromString(null));
    }

    [Fact]
    public void DictionaryConverter_ToAndFromString_RoundTrip()
    {
        var converter = new DictionaryConverter<string, double>(new StringConverter(), new DoubleConverter());
        var original = new Dictionary<string, double> { ["pi"] = 3.14159, ["e"] = 2.71828 };
        var raw = converter.ConvertToString(original);
        var result = converter.ConvertFromString(raw);
        Assert.Equal(original.Count, result!.Count);
        Assert.Equal(original["pi"], result["pi"], 5);
        Assert.Equal(original["e"], result["e"], 5);
    }

    // ── ValueConverterRegistry — collection/dictionary auto-creation ─────────

    [Fact]
    public void ValueConverterRegistry_GetConverter_ReturnsListConverterForListType()
    {
        var conv = ValueConverterRegistry.GetConverter(typeof(List<string>));
        Assert.NotNull(conv);
        Assert.IsType<ListConverter<string>>(conv);
    }

    [Fact]
    public void ValueConverterRegistry_GetConverter_ReturnsListConverterForIListType()
    {
        var conv = ValueConverterRegistry.GetConverter(typeof(IList<int>));
        Assert.NotNull(conv);
        // Should be a ListConverter<int> (registered under IList<int>)
        var result = conv!.ConvertFromString("5,10,15");
        var list = Assert.IsAssignableFrom<IList<int>>(result);
        Assert.Equal(new[] { 5, 10, 15 }, list);
    }

    [Fact]
    public void ValueConverterRegistry_GetConverter_ReturnsArrayConverterForArrayType()
    {
        var conv = ValueConverterRegistry.GetConverter(typeof(string[]));
        Assert.NotNull(conv);
        Assert.IsType<ArrayConverter<string>>(conv);
    }

    [Fact]
    public void ValueConverterRegistry_GetConverter_ReturnsDictionaryConverterForDictionaryType()
    {
        var conv = ValueConverterRegistry.GetConverter(typeof(Dictionary<string, int>));
        Assert.NotNull(conv);
        Assert.IsType<DictionaryConverter<string, int>>(conv);
    }

    [Fact]
    public void ValueConverterRegistry_GetConverter_ReturnsDictionaryConverterForIDictionaryType()
    {
        var conv = ValueConverterRegistry.GetConverter(typeof(IDictionary<string, int>));
        Assert.NotNull(conv);
        var result = conv!.ConvertFromString("k=42");
        var dict = Assert.IsAssignableFrom<IDictionary<string, int>>(result);
        Assert.Equal(42, dict["k"]);
    }

    [Fact]
    public void ValueConverterRegistry_GetConverter_ReturnsNullForUnknownGenericType()
    {
        // HashSet<T> is not a registered collection type
        var conv = ValueConverterRegistry.GetConverter(typeof(HashSet<string>));
        Assert.Null(conv);
    }
}
