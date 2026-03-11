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
}
