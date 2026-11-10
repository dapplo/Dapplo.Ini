// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.WindowsPolicies.Registry;
using Microsoft.Win32;

namespace Dapplo.Ini.WindowsPolicies;

/// <summary>
/// Maps registry policy values to the raw INI text of a property, by the property type. The result goes through the
/// normal value converters and validation. A value of the wrong type or with an invalid value throws
/// <see cref="InvalidSourceValueException"/>, which the configuration reports and skips.
/// </summary>
internal static class PolicyValueMapper
{
    /// <summary>Reads the value of <paramref name="info"/> from <paramref name="sectionKey"/> (the policy key of the section).</summary>
    public static bool TryRead(IRegistryKeyReader sectionKey, PolicyKeyInfo info, out string? raw)
    {
        switch (info.ValueType)
        {
            case PolicyValueType.List:
                return TryReadList(sectionKey, info, out raw);
            case PolicyValueType.Dictionary:
                return TryReadDictionary(sectionKey, info, out raw);
            default:
                if (!sectionKey.TryGetValue(info.Key, out var value, out var kind))
                {
                    raw = null;
                    return false;
                }
                raw = MapScalar(info.Type, info.ValueType, value, kind, info.Key);
                return true;
        }
    }

    private static bool TryReadList(IRegistryKeyReader sectionKey, PolicyKeyInfo info, out string? raw)
    {
        var elementType = Nullable.GetUnderlyingType(info.ElementType ?? typeof(string)) ?? info.ElementType ?? typeof(string);
        var elementValueType = ClassifyScalar(elementType);
        List<string> items;
        if (sectionKey.TryGetValue(info.Key, out var multi, out var kind))
        {
            if (kind != RegistryValueKind.MultiString || multi is not string[] strings)
                throw Invalid(info.Key, $"a list needs a REG_MULTI_SZ value or a sub key '{info.Key}' with the values 1..n, not {kind}", multi);
            items = strings.Select(s => MapScalar(elementType, elementValueType, s, RegistryValueKind.String, info.Key)).ToList();
        }
        else
        {
            using var listKey = sectionKey.OpenSubKey(info.Key);
            if (listKey == null)
            {
                raw = null;
                return false;
            }
            // Values 1..n in numeric order (Group Policy writes them so); other value names are not list items
            var numbered = new List<(long Index, string Name)>();
            foreach (var name in listKey.GetValueNames())
            {
                if (long.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    numbered.Add((index, name));
            }
            items = new List<string>(numbered.Count);
            foreach (var (_, name) in numbered.OrderBy(n => n.Index))
            {
                if (listKey.TryGetValue(name, out var itemValue, out var itemKind))
                    items.Add(MapScalar(elementType, elementValueType, itemValue, itemKind, $"{info.Key}\\{name}"));
            }
        }
        raw = string.Join(info.ListDelimiter.ToString(), items.Select(i => Quote(i, info.ListDelimiter)));
        return true;
    }

    private static bool TryReadDictionary(IRegistryKeyReader sectionKey, PolicyKeyInfo info, out string? raw)
    {
        using var dictionaryKey = sectionKey.OpenSubKey(info.Key);
        if (dictionaryKey == null)
        {
            raw = null;
            return false;
        }
        var valueType = Nullable.GetUnderlyingType(info.DictionaryValueType ?? typeof(string)) ?? info.DictionaryValueType ?? typeof(string);
        var valueKind = ClassifyScalar(valueType);
        var pairs = new List<string>();
        foreach (var name in dictionaryKey.GetValueNames())
        {
            if (name.Length == 0 || !dictionaryKey.TryGetValue(name, out var value, out var kind))
                continue;   // the (Default) value of the key is no entry
            var mapped = MapScalar(valueType, valueKind, value, kind, $"{info.Key}\\{name}");
            pairs.Add(Quote(name, ',', '=') + "=" + Quote(mapped, ',', '='));
        }
        // The inline dictionary format of the INI value converters: key=value,key2=value2
        raw = string.Join(",", pairs);
        return true;
    }

    private static PolicyValueType ClassifyScalar(Type type)
    {
        if (type == typeof(bool)) return PolicyValueType.Boolean;
        if (type.IsEnum) return PolicyValueType.Enum;
        if (PolicyKeyInfo.IsInteger(type)) return PolicyValueType.Integer;
        if (type == typeof(string)) return PolicyValueType.String;
        return PolicyValueType.Text;
    }

    /// <summary>Maps one registry value to INI text for <paramref name="type"/>.</summary>
    internal static string MapScalar(Type type, PolicyValueType valueType, object? value, RegistryValueKind kind, string name)
    {
        switch (valueType)
        {
            case PolicyValueType.Boolean:
                if (kind == RegistryValueKind.DWord && value is int flag)
                {
                    return flag switch
                    {
                        0 => "False",
                        1 => "True",
                        _ => throw Invalid(name, $"a boolean policy must be 0 or 1, not {flag}", value)
                    };
                }
                throw Invalid(name, $"a boolean policy must be a REG_DWORD (0 or 1), not {kind}", value);

            case PolicyValueType.Integer:
                return MapInteger(type, value, kind, name);

            case PolicyValueType.Enum:
                if (kind is RegistryValueKind.String or RegistryValueKind.ExpandString && value is string enumName)
                {
                    if (!TryParseEnum(type, enumName))
                        throw Invalid(name, $"'{enumName}' is not a value of {type.Name}; valid are {string.Join(", ", Enum.GetNames(type))}", value);
                    return enumName.Trim();
                }
                if (kind == RegistryValueKind.DWord && value is int number)
                {
                    var enumValue = Enum.ToObject(type, number);
                    if (!Enum.IsDefined(type, enumValue))
                        throw Invalid(name, $"{number} is not a value of {type.Name}", value);
                    return enumValue.ToString()!;
                }
                throw Invalid(name, $"an enum policy must be a REG_SZ with the name of the value, not {kind}", value);

            case PolicyValueType.String:
                if (kind is RegistryValueKind.String or RegistryValueKind.ExpandString && value is string text)
                    return text;   // REG_EXPAND_SZ is passed on unexpanded, as the INI file would hold it
                throw Invalid(name, $"a string policy must be a REG_SZ or REG_EXPAND_SZ, not {kind}", value);

            default:
                // Other types are written as their INI text; the value converters parse and validate it.
                return kind switch
                {
                    RegistryValueKind.String or RegistryValueKind.ExpandString when value is string s => s,
                    RegistryValueKind.DWord when value is int i => i.ToString(CultureInfo.InvariantCulture),
                    RegistryValueKind.QWord when value is long l => l.ToString(CultureInfo.InvariantCulture),
                    _ => throw Invalid(name, $"a {type.Name} policy must be a REG_SZ with the INI text, not {kind}", value)
                };
        }
    }

    private static string MapInteger(Type type, object? value, RegistryValueKind kind, string name)
    {
        decimal number;
        switch (kind)
        {
            case RegistryValueKind.DWord when value is int dword:
                // REG_DWORD is unsigned; .NET returns it as int
                number = IsSigned(type) ? dword : unchecked((uint)dword);
                break;
            case RegistryValueKind.QWord when value is long qword:
                number = IsSigned(type) ? qword : unchecked((ulong)qword);
                break;
            case RegistryValueKind.String when value is string text
                                               && decimal.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                number = parsed;
                break;
            default:
                throw Invalid(name, $"an integer policy must be a REG_DWORD (REG_QWORD for 64-bit values), not {kind}", value);
        }
        var (min, max) = Range(type);
        if (number < min || number > max)
            throw Invalid(name, $"{number} is out of the range of {type.Name} ({min}..{max})", value);
        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsSigned(Type type) => type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(sbyte);

    private static (decimal Min, decimal Max) Range(Type type)
    {
        if (type == typeof(int)) return (int.MinValue, int.MaxValue);
        if (type == typeof(uint)) return (uint.MinValue, uint.MaxValue);
        if (type == typeof(long)) return (long.MinValue, long.MaxValue);
        if (type == typeof(ulong)) return (ulong.MinValue, ulong.MaxValue);
        if (type == typeof(short)) return (short.MinValue, short.MaxValue);
        if (type == typeof(ushort)) return (ushort.MinValue, ushort.MaxValue);
        if (type == typeof(byte)) return (byte.MinValue, byte.MaxValue);
        return (sbyte.MinValue, sbyte.MaxValue);
    }

    private static bool TryParseEnum(Type type, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        try
        {
            var parsed = Enum.Parse(type, text.Trim(), ignoreCase: true);
            // Numbers parse too: only accept names (or a combination of flag names)
            return !char.IsDigit(text.Trim()[0]) && text.Trim()[0] != '-' && parsed != null;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Quotes a list item or dictionary part when it contains a delimiter, a quote or surrounding spaces.</summary>
    internal static string Quote(string value, char delimiter, char? otherDelimiter = null)
    {
        var needsQuotes = value.Length > 0 && (value.IndexOf(delimiter) >= 0 || value.IndexOf('"') >= 0
                                                || (otherDelimiter.HasValue && value.IndexOf(otherDelimiter.Value) >= 0)
                                                || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1]));
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static InvalidSourceValueException Invalid(string name, string reason, object? value)
        => new($"Policy value '{name}': {reason}.", Describe(value));

    private static string? Describe(object? value) => value switch
    {
        null => null,
        string s => s,
        string[] strings => string.Join(", ", strings),
        byte[] bytes => BitConverter.ToString(bytes),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
