// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Configuration;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.WindowsPolicies;

/// <summary>How a property is stored in the registry.</summary>
public enum PolicyValueType
{
    /// <summary><c>REG_DWORD</c> 0 / 1.</summary>
    Boolean,
    /// <summary><c>REG_DWORD</c> (<c>REG_QWORD</c> for 64-bit types); a numeric <c>REG_SZ</c> is accepted too.</summary>
    Integer,
    /// <summary><c>REG_SZ</c> with the name of the enum value.</summary>
    Enum,
    /// <summary><c>REG_SZ</c> or <c>REG_EXPAND_SZ</c> (never expanded).</summary>
    String,
    /// <summary>A sub key named like the key with the values <c>1</c>..<c>n</c>, or a <c>REG_MULTI_SZ</c> value.</summary>
    List,
    /// <summary>A sub key named like the key: value name = dictionary key.</summary>
    Dictionary,
    /// <summary>Any other type: <c>REG_SZ</c> with the INI text, converted by the normal value converters.</summary>
    Text
}

/// <summary>What the policy support knows about one key of a section, read from the section's meta-model and attributes.</summary>
public sealed class PolicyKeyInfo
{
    internal PolicyKeyInfo(string sectionName, string key, Type propertyType, PropertyInfo? property)
    {
        SectionName = sectionName;
        Key = key;
        PropertyType = propertyType;
        Property = property;
        Type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        ValueType = Classify(Type, out var elementType, out var dictionaryValueType);
        ElementType = elementType;
        DictionaryValueType = dictionaryValueType;
    }

    /// <summary>The INI section name.</summary>
    public string SectionName { get; }

    /// <summary>The INI key (also the registry value or sub key name).</summary>
    public string Key { get; }

    /// <summary>The declared property type.</summary>
    public Type PropertyType { get; }

    /// <summary>The property type without <see cref="Nullable{T}"/>.</summary>
    public Type Type { get; }

    /// <summary>The interface property, when the section is a generated one; otherwise <c>null</c>.</summary>
    public PropertyInfo? Property { get; }

    /// <summary>How the value is stored in the registry.</summary>
    public PolicyValueType ValueType { get; }

    /// <summary>The element type of a list, otherwise <c>null</c>.</summary>
    public Type? ElementType { get; }

    /// <summary>The value type of a dictionary, otherwise <c>null</c>.</summary>
    public Type? DictionaryValueType { get; }

    /// <summary>The list delimiter of the INI value (<c>[IniValue(ListDelimiter = …)]</c>).</summary>
    public char ListDelimiter { get; internal set; } = ',';

    /// <summary>The description (<c>[Description]</c> / <c>[IniValue(Description = …)]</c>).</summary>
    public string? Description { get; internal set; }

    /// <summary>The compiled default as INI text.</summary>
    public string? DefaultValue { get; internal set; }

    /// <summary>The display name: <see cref="PolicyDisplayNameAttribute"/>, otherwise the key split at camel case.</summary>
    public string DisplayName { get; internal set; } = "";

    /// <summary>The extra category from <see cref="PolicyCategoryAttribute"/> (property, else section interface).</summary>
    public string? Category { get; internal set; }

    /// <summary><see cref="PolicyExpandableAttribute"/> is present.</summary>
    public bool IsExpandable { get; internal set; }

    /// <summary><see cref="SecretAttribute"/> is present.</summary>
    public bool IsSecret { get; internal set; }

    /// <summary><see cref="NotAdministrableAttribute"/> is present on the property or the section interface.</summary>
    public bool IsNotAdministrable { get; internal set; }

    /// <summary><c>[IniValue(RuntimeOnly = true)]</c>.</summary>
    public bool IsRuntimeOnly { get; internal set; }

    /// <summary><c>IgnoreConstants</c> on the section or the key.</summary>
    public bool IgnoresConstants { get; internal set; }

    /// <summary><c>IgnoreDefaults</c> on the section or the key.</summary>
    public bool IgnoresDefaults { get; internal set; }

    /// <summary>The minimum of a <c>[Range]</c> attribute.</summary>
    public double? Minimum { get; internal set; }

    /// <summary>The maximum of a <c>[Range]</c> attribute.</summary>
    public double? Maximum { get; internal set; }

    private static PolicyValueType Classify(Type type, out Type? elementType, out Type? dictionaryValueType)
    {
        elementType = null;
        dictionaryValueType = null;
        if (type == typeof(bool)) return PolicyValueType.Boolean;
        if (type.IsEnum) return PolicyValueType.Enum;
        if (IsInteger(type)) return PolicyValueType.Integer;
        if (type == typeof(string)) return PolicyValueType.String;
        if (type.IsArray)
        {
            elementType = type.GetElementType();
            return PolicyValueType.List;
        }
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            {
                dictionaryValueType = arguments[1];
                return PolicyValueType.Dictionary;
            }
            if (definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>)
                || definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>))
            {
                elementType = arguments[0];
                return PolicyValueType.List;
            }
        }
        return PolicyValueType.Text;
    }

    internal static bool IsInteger(Type type)
        => type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
           || type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(sbyte);
}

/// <summary>What the policy support knows about a section.</summary>
public sealed class PolicySectionInfo
{
    internal PolicySectionInfo(IIniSection section, IReadOnlyList<PolicyKeyInfo> keys)
    {
        Section = section;
        Keys = keys;
    }

    /// <summary>The section.</summary>
    public IIniSection Section { get; }

    /// <summary>The section name.</summary>
    public string SectionName => Section.SectionName;

    /// <summary>The keys, in declaration order.</summary>
    public IReadOnlyList<PolicyKeyInfo> Keys { get; }

    /// <summary>The display name: <see cref="PolicyDisplayNameAttribute"/>, otherwise the section name split at camel case.</summary>
    public string DisplayName { get; internal set; } = "";

    /// <summary>The section description.</summary>
    public string? Description { get; internal set; }

    /// <summary><see cref="NotAdministrableAttribute"/> on the section interface.</summary>
    public bool IsNotAdministrable { get; internal set; }

    /// <summary>The interface the section was generated from, when it is a generated section.</summary>
    public Type? Interface { get; internal set; }
}

/// <summary>Reads the policy meta information of sections (reflection on the section interfaces, cached per type).</summary>
public static class PolicyMetadata
{
    private static readonly ConcurrentDictionary<Type, Dictionary<string, PolicyKeyInfo>> KeyCache = new();

    /// <summary>Returns the information about <paramref name="key"/> of <paramref name="section"/>, or <c>null</c> for an unknown key.</summary>
    public static PolicyKeyInfo? GetKey(IIniSection section, string key)
        => GetKeyMap(section).TryGetValue(key, out var info) ? info : null;

    /// <summary>Returns the information about <paramref name="section"/> and all its keys.</summary>
    public static PolicySectionInfo GetSection(IIniSection section)
    {
        var map = GetKeyMap(section);
        var ordered = new List<PolicyKeyInfo>();
        foreach (var key in section.GetKeys())
        {
            if (map.TryGetValue(key, out var info))
                ordered.Add(info);
        }
        var iface = FindSectionInterface(section.GetType());
        var info2 = new PolicySectionInfo(section, ordered)
        {
            Interface = iface,
            Description = (section as IniSectionBase)?.GetSectionDescription(),
            IsNotAdministrable = iface?.GetCustomAttribute<NotAdministrableAttribute>() != null,
            DisplayName = iface?.GetCustomAttribute<PolicyDisplayNameAttribute>()?.DisplayName ?? SplitCamelCase(section.SectionName)
        };
        return info2;
    }

    private static Dictionary<string, PolicyKeyInfo> GetKeyMap(IIniSection section)
        => KeyCache.GetOrAdd(section.GetType(), _ => Build(section));

    private static Dictionary<string, PolicyKeyInfo> Build(IIniSection section)
    {
        var result = new Dictionary<string, PolicyKeyInfo>(StringComparer.OrdinalIgnoreCase);
        var sectionBase = section as IniSectionBase;
        var properties = new Dictionary<string, (PropertyInfo Property, Type Interface)>(StringComparer.OrdinalIgnoreCase);
        foreach (var iface in SectionInterfaces(section.GetType()))
        {
            foreach (var property in iface.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.GetCustomAttribute<IgnoreDataMemberAttribute>() != null)
                    continue;
                var keyName = property.GetCustomAttribute<IniValueAttribute>()?.KeyName
                              ?? property.GetCustomAttribute<DataMemberAttribute>()?.Name
                              ?? property.Name;
                if (!properties.ContainsKey(keyName))
                    properties[keyName] = (property, iface);
            }
        }
        var sectionInterface = FindSectionInterface(section.GetType());
        var sectionCategory = sectionInterface?.GetCustomAttribute<PolicyCategoryAttribute>()?.Category;
        var sectionNotAdministrable = sectionInterface?.GetCustomAttribute<NotAdministrableAttribute>() != null;

        foreach (var key in section.GetKeys())
        {
            properties.TryGetValue(key, out var found);
            var property = found.Property;
            var type = property?.PropertyType ?? section.GetPropertyType(key);
            if (type == null)
                continue;
            var iniValue = property?.GetCustomAttribute<IniValueAttribute>();
            var info = new PolicyKeyInfo(section.SectionName, key, type, property)
            {
                ListDelimiter = iniValue?.ListDelimiter ?? ',',
                Description = sectionBase?.GetPropertyDescription(key) ?? property?.GetCustomAttribute<DescriptionAttribute>()?.Description,
                DefaultValue = iniValue?.DefaultValue ?? FormatDefault(property?.GetCustomAttribute<DefaultValueAttribute>()?.Value),
                DisplayName = property?.GetCustomAttribute<PolicyDisplayNameAttribute>()?.DisplayName ?? SplitCamelCase(key),
                Category = property?.GetCustomAttribute<PolicyCategoryAttribute>()?.Category ?? sectionCategory,
                IsExpandable = property?.GetCustomAttribute<PolicyExpandableAttribute>() != null,
                IsSecret = property?.GetCustomAttribute<SecretAttribute>() != null,
                IsNotAdministrable = sectionNotAdministrable || property?.GetCustomAttribute<NotAdministrableAttribute>() != null,
                IsRuntimeOnly = iniValue?.RuntimeOnly == true,
                IgnoresConstants = (sectionBase?.SectionIgnoresConstants ?? false) || (sectionBase?.IsIgnoreConstantsKey(key) ?? false),
                IgnoresDefaults = (sectionBase?.SectionIgnoresDefaults ?? false) || (sectionBase?.IsIgnoreDefaultsKey(key) ?? false)
            };
            ReadRange(property, info);
            result[key] = info;
        }
        return result;
    }

    private static void ReadRange(PropertyInfo? property, PolicyKeyInfo info)
    {
        if (property == null)
            return;
        // [Range] lives in System.ComponentModel.DataAnnotations, matched by name so no reference is needed
        var range = property.GetCustomAttributes(true)
            .FirstOrDefault(a => a.GetType().FullName == "System.ComponentModel.DataAnnotations.RangeAttribute");
        if (range == null)
            return;
        info.Minimum = ToDouble(range.GetType().GetProperty("Minimum")?.GetValue(range));
        info.Maximum = ToDouble(range.GetType().GetProperty("Maximum")?.GetValue(range));
    }

    private static double? ToDouble(object? value)
    {
        if (value == null)
            return null;
        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static string? FormatDefault(object? value) => value switch
    {
        null => null,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static IEnumerable<Type> SectionInterfaces(Type sectionType)
        => sectionType.GetInterfaces().Where(i => i != typeof(IIniSection) && typeof(IIniSection).IsAssignableFrom(i));

    /// <summary>The most derived section interface: the one no other section interface of the type derives from.</summary>
    private static Type? FindSectionInterface(Type sectionType)
    {
        var interfaces = SectionInterfaces(sectionType).ToList();
        return interfaces.FirstOrDefault(i => !interfaces.Any(other => other != i && i.IsAssignableFrom(other)));
    }

    /// <summary><c>OutputFileFormat</c> → <c>Output file format</c>, <c>HTTPProxy</c> → <c>HTTP proxy</c>.</summary>
    public static string SplitCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_' || c == '.')
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                continue;
            }
            var previous = i > 0 ? name[i - 1] : '\0';
            var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
            var wordStart = i > 0 && char.IsUpper(c)
                            && (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower));
            if (wordStart && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                sb.Append(' ');
            // A word (not an acronym) after the first one starts lower case: "Output file format", "HTTP proxy"
            sb.Append(wordStart && nextIsLower ? char.ToLowerInvariant(c) : c);
        }
        return sb.ToString();
    }
}
