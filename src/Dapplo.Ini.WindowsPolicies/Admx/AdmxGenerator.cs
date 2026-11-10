// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Dapplo.Ini.Configuration;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.WindowsPolicies.Admx;

/// <summary>
/// Generates an administrative template (ADMX + ADML) for the sections of an application, for the Group Policy editor
/// (gpedit.msc, GPMC) and Intune ("Import ADMX"). The registry layout is the one <see cref="WindowsPolicySource"/> reads.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>bool → a policy with enabled value 1 and disabled value 0;</item>
/// <item>integer → <c>decimal</c> (64-bit: <c>longDecimal</c>) with the <c>[Range]</c> as min / max (a negative range: <c>text</c>);</item>
/// <item>enum → <c>enum</c> with one item per name; string → <c>text</c> (<c>expandable</c> with <see cref="PolicyExpandableAttribute"/>);</item>
/// <item>list → <c>list</c> (values 1..n); dictionary → <c>list</c> with <c>explicitValue</c>; other types → <c>text</c> with the INI text.</item>
/// </list>
/// Left out: runtime-only and not administrable keys, secrets, mandatory policies of keys that ignore constants and
/// recommended policies of keys that ignore defaults, and what <see cref="AdmxOptions.Include"/> rejects.
/// </remarks>
public static class AdmxGenerator
{
    /// <summary>The namespace of ADMX and ADML.</summary>
    public static readonly XNamespace PolicyDefinitionsNamespace = "http://schemas.microsoft.com/GroupPolicy/2006/07/PolicyDefinitions";

    private static readonly XNamespace Xsd = "http://www.w3.org/2001/XMLSchema";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private const string SupportedOnName = "SUPPORTED_Product";

    /// <summary>Generates the template for the sections of <paramref name="config"/>.</summary>
    public static AdministrativeTemplate Generate(IniConfig config, AdmxOptions options)
        => Generate((config ?? throw new ArgumentNullException(nameof(config))).GetSections(), options);

    /// <summary>Generates the template for <paramref name="sections"/>.</summary>
    public static AdministrativeTemplate Generate(IEnumerable<IIniSection> sections, AdmxOptions options)
    {
        if (sections == null)
            throw new ArgumentNullException(nameof(sections));
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        if (options.Languages == null || options.Languages.Count == 0)
            throw new ArgumentException("At least one language is needed.", nameof(options));
        return new Builder(options).Build(sections);
    }

    private sealed class Builder
    {
        private readonly AdmxOptions _options;
        private readonly XNamespace _ns = PolicyDefinitionsNamespace;
        // id → English text, in order of creation
        private readonly List<KeyValuePair<string, string>> _strings = new();
        private readonly HashSet<string> _stringIds = new(StringComparer.Ordinal);
        // presentations: id → factory of the element for a language (labels are localized)
        private readonly List<Func<Func<string, string, string>, XElement>> _presentations = new();
        private readonly List<XElement> _categories = new();
        private readonly HashSet<string> _categoryNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _policyNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<XElement> _policies = new();

        public Builder(AdmxOptions options) => _options = options;

        public AdministrativeTemplate Build(IEnumerable<IIniSection> sections)
        {
            var vendorCategory = AddCategory(AdmxOptions.Sanitize(_options.Vendor), _options.Vendor, parent: null);
            var productCategory = AddCategory(AdmxOptions.Sanitize(_options.Product), _options.Product, vendorCategory);
            string? recommendedCategory = null;
            if (_options.IncludeRecommended)
            {
                recommendedCategory = AddCategory(AdmxOptions.Sanitize(_options.Product) + "_Recommended",
                    _options.RecommendedCategoryName ?? $"{_options.Product} - Default Settings (users can override)", vendorCategory);
            }
            AddString(SupportedOnName, _options.SupportedOn ?? _options.Product);

            foreach (var section in sections)
            {
                var sectionInfo = PolicyMetadata.GetSection(section);
                if (sectionInfo.IsNotAdministrable)
                    continue;
                var keys = sectionInfo.Keys.Where(IsAdministrable).ToList();
                if (keys.Count == 0)
                    continue;
                foreach (var kind in new[] { PolicyKind.Mandatory, PolicyKind.Recommended })
                {
                    if (kind == PolicyKind.Recommended && recommendedCategory == null)
                        continue;
                    var root = kind == PolicyKind.Mandatory ? productCategory : recommendedCategory!;
                    foreach (var info in keys)
                    {
                        if (kind == PolicyKind.Mandatory ? info.IgnoresConstants : info.IgnoresDefaults)
                            continue;
                        var category = GetCategory(info, sectionInfo, root);
                        AddPolicy(info, kind, category);
                    }
                }
            }

            var admx = new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement(_ns + "policyDefinitions",
                    new XAttribute(XNamespace.Xmlns + "xsd", Xsd),
                    new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
                    new XAttribute("revision", _options.Revision),
                    new XAttribute("schemaVersion", "1.0"),
                    new XElement(_ns + "policyNamespaces",
                        new XElement(_ns + "target",
                            new XAttribute("prefix", _options.EffectivePrefix),
                            new XAttribute("namespace", _options.EffectiveNamespace))),
                    new XElement(_ns + "resources", new XAttribute("minRequiredRevision", _options.Revision)),
                    new XElement(_ns + "supportedOn",
                        new XElement(_ns + "definitions",
                            new XElement(_ns + "definition",
                                new XAttribute("name", SupportedOnName),
                                new XAttribute("displayName", StringRef(SupportedOnName))))),
                    new XElement(_ns + "categories", _categories),
                    new XElement(_ns + "policies", _policies)));

            var adml = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
            foreach (var language in _options.Languages)
                adml[language] = BuildAdml(language);
            return new AdministrativeTemplate(_options.EffectivePrefix, admx, adml, _policies.Count);
        }

        private bool IsAdministrable(PolicyKeyInfo info)
            => !info.IsRuntimeOnly && !info.IsNotAdministrable && !info.IsSecret
               && (_options.Include == null || _options.Include(info));

        private string GetCategory(PolicyKeyInfo info, PolicySectionInfo section, string root)
        {
            var extra = _options.CategorySelector != null ? _options.CategorySelector(info) : info.Category;
            if (!string.IsNullOrWhiteSpace(extra))
                return AddCategory(root + "_" + AdmxOptions.Sanitize(extra!), extra!.Trim(), root);
            return AddCategory(root + "_" + AdmxOptions.Sanitize(section.SectionName), section.DisplayName, root,
                section.Description);
        }

        private string AddCategory(string name, string displayName, string? parent, string? explain = null)
        {
            if (!_categoryNames.Add(name))
                return name;
            var category = new XElement(_ns + "category",
                new XAttribute("name", name),
                new XAttribute("displayName", StringRef(AddString("Cat_" + name, displayName))));
            if (!string.IsNullOrWhiteSpace(explain))
                category.Add(new XAttribute("explainText", StringRef(AddString("Cat_" + name + "_Explain", explain!))));
            if (parent != null)
                category.Add(new XElement(_ns + "parentCategory", new XAttribute("ref", parent)));
            _categories.Add(category);
            return name;
        }

        private void AddPolicy(PolicyKeyInfo info, PolicyKind kind, string category)
        {
            var baseName = AdmxOptions.Sanitize(info.SectionName) + "_" + AdmxOptions.Sanitize(info.Key)
                           + (kind == PolicyKind.Recommended ? "_Recommended" : "");
            var name = baseName;
            for (var i = 2; !_policyNames.Add(name); i++)
                name = baseName + "_" + i.ToString(CultureInfo.InvariantCulture);

            var sectionPath = _options.EffectiveRootPath
                              + (kind == PolicyKind.Recommended ? "\\" + WindowsPolicyOptions.RecommendedKeyName : "")
                              + "\\" + info.SectionName;

            var policy = new XElement(_ns + "policy",
                new XAttribute("name", name),
                new XAttribute("class", "Both"),
                new XAttribute("displayName", StringRef(AddString(name, info.DisplayName))),
                new XAttribute("explainText", StringRef(AddString(name + "_Explain", Explain(info, kind, sectionPath)))));
            policy.Add(new XAttribute("key", sectionPath));

            var children = new List<XElement>
            {
                new(_ns + "parentCategory", new XAttribute("ref", category)),
                new(_ns + "supportedOn", new XAttribute("ref", SupportedOnName))
            };

            if (info.ValueType == PolicyValueType.Boolean)
            {
                policy.Add(new XAttribute("valueName", info.Key));
                children.Add(new XElement(_ns + "enabledValue", new XElement(_ns + "decimal", new XAttribute("value", 1))));
                children.Add(new XElement(_ns + "disabledValue", new XElement(_ns + "decimal", new XAttribute("value", 0))));
            }
            else
            {
                var (element, presentation) = CreateElement(info, name, sectionPath);
                policy.Add(new XAttribute("presentation", "$(presentation." + name + ")"));
                children.Add(new XElement(_ns + "elements", element));
                _presentations.Add(localize => new XElement(_ns + "presentation", new XAttribute("id", name), presentation(localize)));
            }
            policy.Add(children);
            _policies.Add(policy);
        }

        /// <summary>The element of the policy and the presentation control for it.</summary>
        private (XElement Element, Func<Func<string, string, string>, XElement> Presentation) CreateElement(
            PolicyKeyInfo info, string policyName, string sectionPath)
        {
            var id = AdmxOptions.Sanitize(info.Key);
            var labelId = policyName + "_Label";
            AddString(labelId, info.DisplayName, isPresentationText: true);
            XElement Label(string elementName, Func<string, string, string> localize, params object[] content)
                => new(_ns + elementName, content.Concat(new object[] { localize(labelId, info.DisplayName) }).ToArray());

            switch (info.ValueType)
            {
                case PolicyValueType.Integer when TryGetDecimalRange(info, out var min, out var max, out var isLong):
                {
                    var element = new XElement(_ns + (isLong ? "longDecimal" : "decimal"),
                        new XAttribute("id", id), new XAttribute("valueName", info.Key), new XAttribute("required", "true"),
                        new XAttribute("minValue", min), new XAttribute("maxValue", max));
                    var defaultValue = ulong.TryParse(info.DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)
                                       && d >= min && d <= max ? (ulong?)d : null;
                    return (element, localize => Label(isLong ? "longDecimalTextBox" : "decimalTextBox", localize,
                        new object?[] { new XAttribute("refId", id), defaultValue.HasValue ? new XAttribute("defaultValue", defaultValue.Value) : null }
                            .Where(o => o != null).ToArray()!));
                }

                case PolicyValueType.Enum:
                {
                    var names = Enum.GetNames(info.Type);
                    var element = new XElement(_ns + "enum",
                        new XAttribute("id", id), new XAttribute("valueName", info.Key), new XAttribute("required", "true"));
                    foreach (var enumName in names)
                    {
                        var itemId = policyName + "_" + AdmxOptions.Sanitize(enumName);
                        element.Add(new XElement(_ns + "item",
                            new XAttribute("displayName", StringRef(AddString(itemId, EnumDisplayName(info.Type, enumName)))),
                            new XElement(_ns + "value", new XElement(_ns + "string", enumName))));
                    }
                    var defaultIndex = info.DefaultValue == null ? -1
                        : Array.FindIndex(names, n => string.Equals(n, info.DefaultValue.Trim(), StringComparison.OrdinalIgnoreCase));
                    return (element, localize => Label("dropdownList", localize,
                        new object?[] { new XAttribute("refId", id), new XAttribute("noSort", "true"),
                                defaultIndex >= 0 ? new XAttribute("defaultItem", defaultIndex) : null }
                            .Where(o => o != null).ToArray()!));
                }

                case PolicyValueType.List:
                case PolicyValueType.Dictionary:
                {
                    var element = new XElement(_ns + "list",
                        new XAttribute("id", id),
                        new XAttribute("key", sectionPath + "\\" + info.Key));
                    if (info.ValueType == PolicyValueType.Dictionary)
                        element.Add(new XAttribute("explicitValue", "true"));
                    else
                        element.Add(new XAttribute("valuePrefix", ""));
                    return (element, localize => Label("listBox", localize, new XAttribute("refId", id)));
                }

                default:
                {
                    // string, integers with a negative range and all other types: the INI text
                    var element = new XElement(_ns + "text",
                        new XAttribute("id", id), new XAttribute("valueName", info.Key), new XAttribute("required", "true"));
                    if (info.ValueType == PolicyValueType.String && info.IsExpandable)
                        element.Add(new XAttribute("expandable", "true"));
                    element.Add(new XAttribute("maxLength", 4096));
                    var defaultValue = info.DefaultValue;
                    return (element, localize => new XElement(_ns + "textBox",
                        new XAttribute("refId", id),
                        new XElement(_ns + "label", localize(labelId, info.DisplayName)),
                        string.IsNullOrEmpty(defaultValue) ? null : new XElement(_ns + "defaultValue", defaultValue)));
                }
            }
        }

        private static bool TryGetDecimalRange(PolicyKeyInfo info, out ulong min, out ulong max, out bool isLong)
        {
            var type = info.Type;
            isLong = type == typeof(long) || type == typeof(ulong);
            decimal typeMin, typeMax;
            if (type == typeof(int)) { typeMin = int.MinValue; typeMax = int.MaxValue; }
            else if (type == typeof(uint)) { typeMin = 0; typeMax = uint.MaxValue; }
            else if (type == typeof(long)) { typeMin = long.MinValue; typeMax = long.MaxValue; }
            else if (type == typeof(ulong)) { typeMin = 0; typeMax = ulong.MaxValue; }
            else if (type == typeof(short)) { typeMin = short.MinValue; typeMax = short.MaxValue; }
            else if (type == typeof(ushort)) { typeMin = 0; typeMax = ushort.MaxValue; }
            else if (type == typeof(byte)) { typeMin = 0; typeMax = byte.MaxValue; }
            else { typeMin = sbyte.MinValue; typeMax = sbyte.MaxValue; }

            var low = info.Minimum.HasValue ? Math.Max(typeMin, (decimal)Math.Ceiling(info.Minimum.Value)) : Math.Max(typeMin, 0);
            var high = info.Maximum.HasValue ? Math.Min(typeMax, (decimal)Math.Floor(Math.Min(info.Maximum.Value, (double)decimal.MaxValue))) : typeMax;
            min = max = 0;
            // A decimal element is unsigned: a range that allows negative values needs a text element
            if (info.Minimum is < 0 || low > high)
                return false;
            min = (ulong)low;
            max = (ulong)high;
            return true;
        }

        private static string EnumDisplayName(Type enumType, string name)
        {
            var field = enumType.GetField(name, BindingFlags.Public | BindingFlags.Static);
            return field?.GetCustomAttribute<PolicyDisplayNameAttribute>()?.DisplayName
                   ?? field?.GetCustomAttribute<DescriptionAttribute>()?.Description
                   ?? PolicyMetadata.SplitCamelCase(name);
        }

        private string Explain(PolicyKeyInfo info, PolicyKind kind, string sectionPath)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(info.Description))
                sb.Append(info.Description!.Trim()).Append("\n\n");
            sb.Append(kind == PolicyKind.Mandatory
                ? "When this policy is configured, users can't change the setting."
                : "When this policy is configured, it is the default; users can change the setting.");
            if (info.ValueType == PolicyValueType.Boolean)
                sb.Append(" Enabled turns the setting on, disabled turns it off.");
            if (_options.ExplainDefaultValue && !string.IsNullOrEmpty(info.DefaultValue))
                sb.Append("\n\nDefault: ").Append(info.DefaultValue);
            if (_options.ExplainRegistryLocation)
                sb.Append("\n\nRegistry: ").Append(sectionPath).Append('\\').Append(info.Key)
                    .Append(" (INI: [").Append(info.SectionName).Append("] ").Append(info.Key).Append(')');
            return sb.ToString();
        }

        private static string StringRef(string id) => "$(string." + id + ")";

        private string AddString(string id, string text, bool isPresentationText = false)
        {
            if (_stringIds.Add(id) && !isPresentationText)
                _strings.Add(new KeyValuePair<string, string>(id, text));
            return id;
        }

        private XDocument BuildAdml(string language)
        {
            string Localize(string id, string text) => _options.Localize?.Invoke(language, id, text) ?? text;
            var stringTable = new XElement(_ns + "stringTable",
                _strings.Select(s => new XElement(_ns + "string", new XAttribute("id", s.Key), Localize(s.Key, s.Value))));
            var presentationTable = new XElement(_ns + "presentationTable", _presentations.Select(p => p(Localize)));
            return new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement(_ns + "policyDefinitionResources",
                    new XAttribute(XNamespace.Xmlns + "xsd", Xsd),
                    new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
                    new XAttribute("revision", _options.Revision),
                    new XAttribute("schemaVersion", "1.0"),
                    new XElement(_ns + "displayName", Localize("TemplateDisplayName", $"{_options.Product} policies")),
                    new XElement(_ns + "description", Localize("TemplateDescription", $"Policies of {_options.Vendor} {_options.Product}.")),
                    new XElement(_ns + "resources", stringTable, presentationTable)));
        }
    }
}
