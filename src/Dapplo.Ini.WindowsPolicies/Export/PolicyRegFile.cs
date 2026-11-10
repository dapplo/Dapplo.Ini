// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections;
using System.Globalization;
using System.Text;
using Dapplo.Ini.Configuration;
using Dapplo.Ini.Interfaces;
using Microsoft.Win32;

namespace Dapplo.Ini.WindowsPolicies.Export;

/// <summary>Options for <see cref="PolicyRegFile"/>.</summary>
public sealed class PolicyRegFileOptions
{
    /// <summary>Mandatory (default) or recommended policies.</summary>
    public PolicyKind Kind { get; set; } = PolicyKind.Mandatory;

    /// <summary>The hive; default <see cref="RegistryHive.LocalMachine"/>.</summary>
    public RegistryHive Hive { get; set; } = RegistryHive.LocalMachine;

    /// <summary>The policy root below the hive, overriding <c>SOFTWARE\Policies\&lt;Vendor&gt;\&lt;Product&gt;</c>.</summary>
    public string? RootPath { get; set; }

    /// <summary>Export only the keys the user set (<see cref="IIniSection.IsUserValue"/>). Default <c>false</c>: every key.</summary>
    public bool OnlyUserValues { get; set; }

    /// <summary>Export secrets (<see cref="SecretAttribute"/>) too. Default <c>false</c>.</summary>
    public bool IncludeSecrets { get; set; }

    /// <summary>An extra filter of your own; <c>false</c> leaves the key out.</summary>
    public Func<PolicyKeyInfo, bool>? Include { get; set; }
}

/// <summary>
/// Writes the current values of sections as a <c>.reg</c> file in the policy layout, e.g. to roll out the settings of a
/// configured installation, or to test the policies of an application.
/// </summary>
/// <remarks>
/// Keys that are runtime-only or not administrable are left out, and secrets unless asked for. Lists and dictionaries
/// are written as a sub key, which is deleted first so no stale entries remain.
/// </remarks>
public static class PolicyRegFile
{
    /// <summary>The first line of a <c>.reg</c> file.</summary>
    public const string Header = "Windows Registry Editor Version 5.00";

    /// <summary>Creates the content of the <c>.reg</c> file for <paramref name="sections"/>.</summary>
    public static string Create(IEnumerable<IIniSection> sections, string vendor, string product, PolicyRegFileOptions? options = null)
    {
        if (sections == null)
            throw new ArgumentNullException(nameof(sections));
        options ??= new PolicyRegFileOptions();
        var rootPath = string.IsNullOrWhiteSpace(options.RootPath)
            ? WindowsPolicyOptions.GetDefaultRootPath(vendor, product)
            : options.RootPath!.Trim('\\');
        var hiveName = options.Hive switch
        {
            RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
            RegistryHive.CurrentUser => "HKEY_CURRENT_USER",
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Hive, "Policies live in HKEY_LOCAL_MACHINE or HKEY_CURRENT_USER.")
        };
        var basePath = hiveName + "\\" + rootPath + (options.Kind == PolicyKind.Recommended ? "\\" + WindowsPolicyOptions.RecommendedKeyName : "");

        var sb = new StringBuilder();
        sb.Append(Header).Append("\r\n");
        foreach (var section in sections)
        {
            var sectionInfo = PolicyMetadata.GetSection(section);
            if (sectionInfo.IsNotAdministrable)
                continue;
            var sectionPath = basePath + "\\" + section.SectionName;
            var values = new StringBuilder();
            var subKeys = new StringBuilder();
            foreach (var info in sectionInfo.Keys)
            {
                if (info.IsRuntimeOnly || info.IsNotAdministrable || (info.IsSecret && !options.IncludeSecrets)
                    || (options.OnlyUserValues && !section.IsUserValue(info.Key))
                    || (options.Include != null && !options.Include(info)))
                    continue;
                WriteKey(section, info, sectionPath, values, subKeys);
            }
            if (values.Length == 0 && subKeys.Length == 0)
                continue;
            sb.Append("\r\n[").Append(sectionPath).Append("]\r\n").Append(values).Append(subKeys);
        }
        return sb.ToString();
    }

    /// <summary>Writes the <c>.reg</c> file (UTF-16 with byte order mark, as regedit writes it).</summary>
    public static void Write(string path, IEnumerable<IIniSection> sections, string vendor, string product, PolicyRegFileOptions? options = null)
        => File.WriteAllText(path, Create(sections, vendor, product, options), new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

    private static void WriteKey(IIniSection section, PolicyKeyInfo info, string sectionPath, StringBuilder values, StringBuilder subKeys)
    {
        var value = section.GetValue(info.Key);
        if (value == null)
            return;
        switch (info.ValueType)
        {
            case PolicyValueType.List:
            {
                var listPath = sectionPath + "\\" + info.Key;
                subKeys.Append("\r\n[-").Append(listPath).Append("]\r\n");
                subKeys.Append("\r\n[").Append(listPath).Append("]\r\n");
                var index = 1;
                foreach (var item in (IEnumerable)value)
                {
                    if (item == null)
                        continue;
                    subKeys.Append(ValueLine(index.ToString(CultureInfo.InvariantCulture), item, ElementInfo(info.ElementType)));
                    index++;
                }
                break;
            }
            case PolicyValueType.Dictionary:
            {
                var dictionaryPath = sectionPath + "\\" + info.Key;
                subKeys.Append("\r\n[-").Append(dictionaryPath).Append("]\r\n");
                subKeys.Append("\r\n[").Append(dictionaryPath).Append("]\r\n");
                foreach (DictionaryEntry entry in (IDictionary)value)
                {
                    if (entry.Value == null)
                        continue;
                    var name = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "";
                    if (name.Length > 0)
                        subKeys.Append(ValueLine(name, entry.Value, ElementInfo(info.DictionaryValueType)));
                }
                break;
            }
            case PolicyValueType.Text:
            {
                // The INI text, as the value converters read it back
                var raw = section.GetRawValue(info.Key)
                          ?? (value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString());
                if (raw != null)
                    values.Append(StringLine(info.Key, raw, expandable: false));
                break;
            }
            default:
                values.Append(ValueLine(info.Key, value, (info.Type, info.ValueType, info.IsExpandable)));
                break;
        }
    }

    private static (Type Type, PolicyValueType ValueType, bool Expandable) ElementInfo(Type? type)
    {
        var t = type == null ? typeof(string) : Nullable.GetUnderlyingType(type) ?? type;
        PolicyValueType kind;
        if (t == typeof(bool)) kind = PolicyValueType.Boolean;
        else if (t.IsEnum) kind = PolicyValueType.Enum;
        else if (PolicyKeyInfo.IsInteger(t)) kind = PolicyValueType.Integer;
        else if (t == typeof(string)) kind = PolicyValueType.String;
        else kind = PolicyValueType.Text;
        return (t, kind, false);
    }

    private static string ValueLine(string name, object value, (Type Type, PolicyValueType ValueType, bool Expandable) info)
    {
        switch (info.ValueType)
        {
            case PolicyValueType.Boolean:
                return DWordLine(name, (bool)value ? 1u : 0u);
            case PolicyValueType.Integer:
                if (info.Type == typeof(long) || info.Type == typeof(ulong))
                    return QWordLine(name, info.Type == typeof(long) ? unchecked((ulong)(long)value) : (ulong)value);
                return DWordLine(name, info.Type == typeof(uint) ? (uint)value : unchecked((uint)Convert.ToInt32(value, CultureInfo.InvariantCulture)));
            case PolicyValueType.Enum:
                return StringLine(name, value.ToString()!, expandable: false);
            default:
                var text = value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? "";
                return StringLine(name, text, info.Expandable);
        }
    }

    private static string DWordLine(string name, uint value)
        => $"{Name(name)}=dword:{value.ToString("x8", CultureInfo.InvariantCulture)}\r\n";

    private static string QWordLine(string name, ulong value)
        => $"{Name(name)}=hex(b):{Hex(BitConverter.GetBytes(value))}\r\n";

    private static string StringLine(string name, string value, bool expandable)
    {
        if (expandable)
            return $"{Name(name)}=hex(2):{Hex(Encoding.Unicode.GetBytes(value + "\0"))}\r\n";
        if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\0') >= 0)
            return $"{Name(name)}=hex(1):{Hex(Encoding.Unicode.GetBytes(value + "\0"))}\r\n";   // REG_SZ, as regedit writes multi line text
        return $"{Name(name)}=\"{Escape(value)}\"\r\n";
    }

    private static string Name(string name) => "\"" + Escape(name) + "\"";

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Hex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
