// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Dapplo.Ini.Generator;

/// <summary>
/// Incremental source generator that creates a concrete class for every interface that
/// either carries <c>[IniSection]</c> or directly extends <see cref="IIniSection"/>.
/// </summary>
[Generator]
public sealed class IniSectionGenerator : IIncrementalGenerator
{
    private const string IniSectionAttributeFqn  = "Dapplo.Ini.Attributes.IniSectionAttribute";
    private const string IniValueAttributeFqn    = "Dapplo.Ini.Attributes.IniValueAttribute";
    private const string IIniSectionFqn          = "Dapplo.Ini.Interfaces.IIniSection";

    // FQNs for standard .NET attributes whose semantics we honour in addition to our own
    private const string DefaultValueAttributeFqn    = "System.ComponentModel.DefaultValueAttribute";
    private const string DescriptionAttributeFqn     = "System.ComponentModel.DescriptionAttribute";
    private const string DataContractAttributeFqn    = "System.Runtime.Serialization.DataContractAttribute";
    private const string DataMemberAttributeFqn      = "System.Runtime.Serialization.DataMemberAttribute";
    private const string IgnoreDataMemberAttributeFqn = "System.Runtime.Serialization.IgnoreDataMemberAttribute";
    private const string RequiredAttributeFqn        = "System.ComponentModel.DataAnnotations.RequiredAttribute";
    private const string RangeAttributeFqn           = "System.ComponentModel.DataAnnotations.RangeAttribute";
    private const string MaxLengthAttributeFqn       = "System.ComponentModel.DataAnnotations.MaxLengthAttribute";
    private const string RegularExpressionAttributeFqn = "System.ComponentModel.DataAnnotations.RegularExpressionAttribute";

    // FQNs for property-change notification interfaces
    private const string INotifyPropertyChangedFqn  = "System.ComponentModel.INotifyPropertyChanged";
    private const string INotifyPropertyChangingFqn = "System.ComponentModel.INotifyPropertyChanging";

    // FQNs used for non-generic (dispatch) lifecycle interfaces
    private const string IAfterLoadFqn        = "Dapplo.Ini.Interfaces.IAfterLoad";
    private const string IBeforeSaveFqn       = "Dapplo.Ini.Interfaces.IBeforeSave";
    private const string IAfterSaveFqn        = "Dapplo.Ini.Interfaces.IAfterSave";
    private const string ITransactionalFqn    = "Dapplo.Ini.Interfaces.ITransactional";
    private const string IDataValidationFqn   = "Dapplo.Ini.Interfaces.IDataValidation";
    private const string IUnknownKeyFqn       = "Dapplo.Ini.Interfaces.IUnknownKey";

    // Names of the generic (static-virtual) lifecycle interfaces
    private const string IAfterLoadGenericName      = "IAfterLoad";
    private const string IBeforeSaveGenericName     = "IBeforeSave";
    private const string IAfterSaveGenericName      = "IAfterSave";
    private const string IDataValidationGenericName = "IDataValidation";
    private const string IUnknownKeyGenericName     = "IUnknownKey";
    private const string LifecycleInterfacesNamespace = "Dapplo.Ini.Interfaces";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Filter for interface declarations that either:
        // (a) carry [IniSection], OR
        // (b) extend at least one other interface (which may be IIniSection)
        // The transform step narrows further to IIniSection-implementing interfaces only.
        var interfaces = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InterfaceDeclarationSyntax ids
                                               && (ids.AttributeLists.Count > 0
                                                   || ids.BaseList != null),
                transform: static (ctx, _) => GetInterfaceModel(ctx))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutput(interfaces, static (spc, model) =>
            spc.AddSource($"{model.GeneratedClassName}.g.cs",
                SourceText.From(Emit(model), Encoding.UTF8)));
    }

    // ── Model ─────────────────────────────────────────────────────────────────

    private sealed class PropertyModel
    {
        public string Name { get; set; } = "";
        public string TypeFullName { get; set; } = "";
        public string? KeyName { get; set; }
        public string? DefaultValue { get; set; }
        public string? Description { get; set; }
        public bool IsTransactional { get; set; }
        /// <summary>When true, suppresses <c>PropertyChanged</c> for this property even when the section interface extends <c>INotifyPropertyChanged</c>.</summary>
        public bool SuppressPropertyChanged { get; set; }
        /// <summary>When true, suppresses <c>PropertyChanging</c> for this property even when the section interface extends <c>INotifyPropertyChanging</c>.</summary>
        public bool SuppressPropertyChanging { get; set; }
        public bool IsReadOnly { get; set; }
        // True when property type is a value type (needs different nullability handling)
        public bool IsValueType { get; set; }
        // True when the property is a string-keyed dictionary (Dictionary<string,TV> or IDictionary<string,TV>).
        // Such properties use dotted sub-key notation in the INI file (e.g. "Config.timeout = 30")
        // rather than packing all pairs into a single value string.
        public bool IsSubKeyDictionary { get; set; }
        // Full C# type name of the dictionary value type (e.g. "int") when IsSubKeyDictionary is true.
        public string? DictionaryValueTypeFullName { get; set; }
        // True when [IgnoreDataMember] is present — property is excluded from INI read/write.
        public bool IsIgnored { get; set; }
        // True when [IniValue(RuntimeOnly=true)] — property has a default and participates in
        // ResetToDefaults but is never loaded from or saved to the INI file.
        public bool IsRuntimeOnly { get; set; }
        // True when [IniValue(EmptyWhenNull=true)] — a null/absent raw value produces an empty
        // result (string.Empty, empty list, empty array, empty dictionary) instead of null.
        public bool EmptyWhenNull { get; set; }
        // True when [IniValue(IgnoreDefaults=true)] — property is never set from defaults files.
        public bool IgnoreDefaults { get; set; }
        // True when [IniValue(IgnoreConstants=true)] — property is never set from constants files.
        public bool IgnoreConstants { get; set; }
        // True when property type is list-like (List<T>, IList<T>, ICollection<T>, IEnumerable<T>,
        // IReadOnlyList<T>, IReadOnlyCollection<T>, or T[]). Supports per-property ListDelimiter.
        public bool IsListLike { get; set; }
        public char ListDelimiter { get; set; } = ',';
        public string? WriterQuoteValues { get; set; }
        public string? WriterEscapeSequences { get; set; }
        public string? WriterComments { get; set; }
        // Validation attributes from System.ComponentModel.DataAnnotations
        public bool IsRequired { get; set; }
        public string? RequiredErrorMessage { get; set; }
        public string? RangeMinRaw { get; set; }
        public string? RangeMaxRaw { get; set; }
        public string? RangeErrorMessage { get; set; }
        public int? MaxLength { get; set; }
        public string? MaxLengthErrorMessage { get; set; }
        public string? RegexPattern { get; set; }
        public string? RegexErrorMessage { get; set; }
        // Convenience: true when any DataAnnotations validation attributes are present
        public bool HasValidationAttributes => IsRequired || RangeMinRaw != null || MaxLength.HasValue || RegexPattern != null;
    }

    private sealed class SectionModel
    {
        public string Namespace { get; set; } = "";
        public string InterfaceName { get; set; } = "";
        public string GeneratedClassName { get; set; } = "";
        public string SectionName { get; set; } = "";
        public string? Description { get; set; }
        public bool ImplementsTransactional { get; set; }
        // Non-generic (old partial-class pattern): consumer provides instance method in partial class
        public bool ImplementsBeforeSave { get; set; }
        public bool ImplementsAfterSave { get; set; }
        public bool ImplementsAfterLoad { get; set; }
        // Generic (new static-virtual pattern): generator emits bridge; consumer overrides static method in interface
        public bool ImplementsAfterLoadGeneric { get; set; }
        public bool ImplementsBeforeSaveGeneric { get; set; }
        public bool ImplementsAfterSaveGeneric { get; set; }
        // Data-validation (INotifyDataErrorInfo)
        public bool ImplementsDataValidationGeneric { get; set; }
        public bool ImplementsDataValidation { get; set; }
        // Unknown-key migration
        public bool ImplementsUnknownKeyGeneric { get; set; }
        public bool ImplementsUnknownKey { get; set; }
        // True when any property carries DataAnnotations validation attributes
        public bool HasAttributeBasedValidation { get; set; }
        // True when [IniSection(EmptyWhenNull=true)] — propagates to all non-value-type properties
        public bool SectionEmptyWhenNull { get; set; }
        // True when [IniSection(IgnoreDefaults=true)] — section is never populated from defaults files
        public bool SectionIgnoresDefaults { get; set; }
        // True when [IniSection(IgnoreConstants=true)] — section is never populated from constants files
        public bool SectionIgnoresConstants { get; set; }
        public string? SectionWriterQuoteValues { get; set; }
        public string? SectionWriterEscapeSequences { get; set; }
        public string? SectionWriterComments { get; set; }
        // True when the section interface extends INotifyPropertyChanged / INotifyPropertyChanging
        public bool ImplementsINotifyPropertyChanged { get; set; }
        public bool ImplementsINotifyPropertyChanging { get; set; }
        public List<PropertyModel> Properties { get; set; } = new();
    }

    // ── Extraction ────────────────────────────────────────────────────────────

    private static SectionModel? GetInterfaceModel(GeneratorSyntaxContext ctx)
    {
        var ids = (InterfaceDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(ids) as INamedTypeSymbol;
        if (symbol is null) return null;

        // Accept the interface when it carries [IniSection] OR when it directly or
        // indirectly extends IIniSection (without requiring the attribute).
        // IIniSection itself is excluded — we only generate for consumer interfaces.
        var iniSectionAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == IniSectionAttributeFqn);

        bool implementsIIniSection = symbol.ToDisplayString() != IIniSectionFqn
            && symbol.AllInterfaces.Any(i => i.ToDisplayString() == IIniSectionFqn);

        if (iniSectionAttr is null && !implementsIIniSection) return null;

        var interfaceName = symbol.Name;
        var namespaceName = symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();

        // Determine section name: [IniSection] arg → [DataContract] Name → strip leading 'I' → use interface name
        string sectionName;
        if (iniSectionAttr?.ConstructorArguments.Length > 0 &&
            iniSectionAttr.ConstructorArguments[0].Value is string sn && !string.IsNullOrEmpty(sn))
            sectionName = sn;
        else
        {
            // Fall back to [DataContract(Name="...")] if present.
            // Note: in most .NET runtime versions DataContractAttribute does not allow
            // AttributeTargets.Interface, so this path is rarely exercised.  The logic is
            // retained here for forward-compatibility and edge cases.
            var dataContractAttr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DataContractAttributeFqn);
            string? dataContractName = null;
            if (dataContractAttr != null)
                foreach (var na in dataContractAttr.NamedArguments)
                    if (na.Key == "Name" && na.Value.Value is string dcn && !string.IsNullOrEmpty(dcn))
                        dataContractName = dcn;

            if (dataContractName != null)
                sectionName = dataContractName;
            else if (interfaceName.StartsWith("I") && interfaceName.Length > 1)
                sectionName = interfaceName.Substring(1);
            else
                sectionName = interfaceName;
        }

        string? description = null;
        bool sectionEmptyWhenNull = false;
        bool sectionIgnoresDefaults = false;
        bool sectionIgnoresConstants = false;
        string? sectionWriterQuoteValues = null;
        string? sectionWriterEscapeSequences = null;
        string? sectionWriterComments = null;
        if (iniSectionAttr != null)
            foreach (var na in iniSectionAttr.NamedArguments)
            {
                if (na.Key == "Description" && na.Value.Value is string d)
                    description = d;
                if (na.Key == "EmptyWhenNull" && na.Value.Value is true)
                    sectionEmptyWhenNull = true;
                if (na.Key == "IgnoreDefaults" && na.Value.Value is true)
                    sectionIgnoresDefaults = true;
                if (na.Key == "IgnoreConstants" && na.Value.Value is true)
                    sectionIgnoresConstants = true;
                if (na.Key == "QuoteValues" && na.Value.Value != null)
                    sectionWriterQuoteValues = GetEnumValueName(na.Value);
                if (na.Key == "EscapeSequences" && na.Value.Value != null)
                    sectionWriterEscapeSequences = GetEnumValueName(na.Value);
                if (na.Key == "WriteComments" && na.Value.Value != null)
                    sectionWriterComments = GetEnumValueName(na.Value);
            }

        // Fall back to [Description("...")] on the interface if [IniSection] doesn't specify Description
        if (description == null)
        {
            var descAttrOnIface = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DescriptionAttributeFqn);
            if (descAttrOnIface != null && descAttrOnIface.ConstructorArguments.Length > 0 &&
                descAttrOnIface.ConstructorArguments[0].Value is string ifaceDesc)
                description = ifaceDesc;
        }

        // Check which additional interfaces are implemented
        bool implementsTransactional      = ImplementsInterface(symbol, ITransactionalFqn);
        // Non-generic (old pattern): check for non-generic lifecycle interfaces
        // A generic IAfterLoad<TSelf> also satisfies the non-generic check, so we detect generic first.
        bool implementsAfterLoadGeneric   = ImplementsGenericInterface(symbol, LifecycleInterfacesNamespace, IAfterLoadGenericName);
        bool implementsBeforeSaveGeneric  = ImplementsGenericInterface(symbol, LifecycleInterfacesNamespace, IBeforeSaveGenericName);
        bool implementsAfterSaveGeneric   = ImplementsGenericInterface(symbol, LifecycleInterfacesNamespace, IAfterSaveGenericName);
        // Non-generic only when not using the generic version
        bool implementsAfterLoad   = !implementsAfterLoadGeneric  && ImplementsInterface(symbol, IAfterLoadFqn);
        bool implementsBeforeSave  = !implementsBeforeSaveGeneric && ImplementsInterface(symbol, IBeforeSaveFqn);
        bool implementsAfterSave   = !implementsAfterSaveGeneric  && ImplementsInterface(symbol, IAfterSaveFqn);

        // Data validation
        bool implementsDataValidationGeneric = ImplementsGenericInterface(symbol, LifecycleInterfacesNamespace, IDataValidationGenericName);
        bool implementsDataValidation        = !implementsDataValidationGeneric && ImplementsInterface(symbol, IDataValidationFqn);

        // Unknown-key migration
        bool implementsUnknownKeyGeneric = ImplementsGenericInterface(symbol, LifecycleInterfacesNamespace, IUnknownKeyGenericName);
        bool implementsUnknownKey        = !implementsUnknownKeyGeneric && ImplementsInterface(symbol, IUnknownKeyFqn);

        // Property-change notification: interface-level opt-in.
        // Events are generated for all properties when the section interface extends the corresponding
        // BCL interface.  Individual properties may suppress events via [IniValue(SuppressPropertyChanged/Changing=true)].
        bool implementsINotifyPropertyChanged  = ImplementsInterface(symbol, INotifyPropertyChangedFqn);
        bool implementsINotifyPropertyChanging = ImplementsInterface(symbol, INotifyPropertyChangingFqn);

        var properties = new List<PropertyModel>();
        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            // Skip properties marked [IgnoreDataMember]
            bool isIgnored = member.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == IgnoreDataMemberAttributeFqn);

            var prop = new PropertyModel
            {
                Name = member.Name,
                TypeFullName = member.Type.ToDisplayString(),
                IsValueType  = member.Type.IsValueType,
                IsIgnored    = isIgnored,
                // A getter-only interface property ({ get; }) is treated as read-only:
                // the value is loaded from the INI file and defaults are applied, but
                // it is never written back to disk.  The generated implementation still
                // exposes a public setter so the framework (and callers with access to
                // the concrete class) can assign values; the setter is simply not part
                // of the interface contract.
                IsReadOnly   = member.SetMethod == null
            };

            // Detect string-keyed dictionaries: Dictionary<string, TV> and IDictionary<string, TV>.
            // These use dotted sub-key notation in the INI file instead of a packed single value.
            if (member.Type is IArrayTypeSymbol)
            {
                prop.IsListLike = true;
            }

            if (member.Type is INamedTypeSymbol namedMemberType && namedMemberType.IsGenericType)
            {
                var originalDefStr = namedMemberType.OriginalDefinition.ToDisplayString();
                if (namedMemberType.TypeArguments.Length == 2 &&
                    namedMemberType.TypeArguments[0].SpecialType == SpecialType.System_String &&
                    (originalDefStr == "System.Collections.Generic.Dictionary<TKey, TValue>" ||
                     originalDefStr == "System.Collections.Generic.IDictionary<TKey, TValue>"))
                {
                    prop.IsSubKeyDictionary = true;
                    prop.DictionaryValueTypeFullName = namedMemberType.TypeArguments[1].ToDisplayString();
                }

                if (namedMemberType.TypeArguments.Length == 1 &&
                    (originalDefStr == "System.Collections.Generic.List<T>" ||
                     originalDefStr == "System.Collections.Generic.IList<T>" ||
                     originalDefStr == "System.Collections.Generic.ICollection<T>" ||
                     originalDefStr == "System.Collections.Generic.IEnumerable<T>" ||
                     originalDefStr == "System.Collections.Generic.IReadOnlyList<T>" ||
                     originalDefStr == "System.Collections.Generic.IReadOnlyCollection<T>"))
                {
                    prop.IsListLike = true;
                }
            }

            // ── Collect [IniValue] attribute (takes precedence) ──────────────────
            var iniValueAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == IniValueAttributeFqn);
            if (iniValueAttr != null)
            {
                foreach (var na in iniValueAttr.NamedArguments)
                {
                    switch (na.Key)
                    {
                        case "KeyName":                 prop.KeyName = na.Value.Value as string; break;
                        case "DefaultValue":            prop.DefaultValue = na.Value.Value as string; break;
                        case "Description":             prop.Description = na.Value.Value as string; break;
                        case "Transactional":           prop.IsTransactional = na.Value.Value is true; break;
                        case "SuppressPropertyChanged": prop.SuppressPropertyChanged = na.Value.Value is true; break;
                        case "SuppressPropertyChanging": prop.SuppressPropertyChanging = na.Value.Value is true; break;
                        case "ReadOnly":                prop.IsReadOnly = na.Value.Value is true; break;
                        case "RuntimeOnly":             prop.IsRuntimeOnly = na.Value.Value is true; break;
                        case "EmptyWhenNull":           prop.EmptyWhenNull = na.Value.Value is true; break;
                        case "IgnoreDefaults":          prop.IgnoreDefaults = na.Value.Value is true; break;
                        case "IgnoreConstants":         prop.IgnoreConstants = na.Value.Value is true; break;
                        case "ListDelimiter":           if (na.Value.Value is char c) prop.ListDelimiter = c; break;
                        case "QuoteValues":             prop.WriterQuoteValues = GetEnumValueName(na.Value); break;
                        case "EscapeSequences":         prop.WriterEscapeSequences = GetEnumValueName(na.Value); break;
                        case "WriteComments":           prop.WriterComments = GetEnumValueName(na.Value); break;
                    }
                }
            }

            // ── Standard .NET attribute fallbacks ────────────────────────────────

            // [DataMember(Name="...")] → KeyName fallback (only when [IniValue] didn't set it)
            if (prop.KeyName == null)
            {
                var dataMemberAttr = member.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DataMemberAttributeFqn);
                if (dataMemberAttr != null)
                    foreach (var na in dataMemberAttr.NamedArguments)
                        if (na.Key == "Name" && na.Value.Value is string dmName && !string.IsNullOrEmpty(dmName))
                            prop.KeyName = dmName;
            }

            // [DefaultValue(...)] → DefaultValue fallback
            if (prop.DefaultValue == null)
            {
                var defaultValueAttr = member.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DefaultValueAttributeFqn);
                if (defaultValueAttr != null && defaultValueAttr.ConstructorArguments.Length > 0)
                {
                    var raw = defaultValueAttr.ConstructorArguments[0].Value;
                    prop.DefaultValue = raw == null ? null : FormatDefaultValueAsString(raw);
                }
            }

            // [Description("...")] → Description fallback
            if (prop.Description == null)
            {
                var descAttr = member.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DescriptionAttributeFqn);
                if (descAttr != null && descAttr.ConstructorArguments.Length > 0 &&
                    descAttr.ConstructorArguments[0].Value is string desc)
                    prop.Description = desc;
            }

            // ── DataAnnotations validation attributes ────────────────────────────

            // [Required]
            var requiredAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == RequiredAttributeFqn);
            if (requiredAttr != null)
            {
                prop.IsRequired = true;
                foreach (var na in requiredAttr.NamedArguments)
                    if (na.Key == "ErrorMessage" && na.Value.Value is string em)
                        prop.RequiredErrorMessage = em;
            }

            // [Range(min, max)]
            var rangeAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == RangeAttributeFqn);
            if (rangeAttr != null && rangeAttr.ConstructorArguments.Length >= 2)
            {
                prop.RangeMinRaw = FormatRangeArgAsLiteral(rangeAttr.ConstructorArguments[0]);
                prop.RangeMaxRaw = FormatRangeArgAsLiteral(rangeAttr.ConstructorArguments[1]);
                foreach (var na in rangeAttr.NamedArguments)
                    if (na.Key == "ErrorMessage" && na.Value.Value is string em)
                        prop.RangeErrorMessage = em;
            }

            // [MaxLength(n)]
            var maxLengthAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == MaxLengthAttributeFqn);
            if (maxLengthAttr != null && maxLengthAttr.ConstructorArguments.Length > 0 &&
                maxLengthAttr.ConstructorArguments[0].Value is int ml)
            {
                prop.MaxLength = ml;
                foreach (var na in maxLengthAttr.NamedArguments)
                    if (na.Key == "ErrorMessage" && na.Value.Value is string em)
                        prop.MaxLengthErrorMessage = em;
            }

            // [RegularExpression(pattern)]
            var regexAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == RegularExpressionAttributeFqn);
            if (regexAttr != null && regexAttr.ConstructorArguments.Length > 0 &&
                regexAttr.ConstructorArguments[0].Value is string pattern)
            {
                prop.RegexPattern = pattern;
                foreach (var na in regexAttr.NamedArguments)
                    if (na.Key == "ErrorMessage" && na.Value.Value is string em)
                        prop.RegexErrorMessage = em;
            }

            properties.Add(prop);
        }

        // Propagate section-level EmptyWhenNull to all non-value-type properties.
        // This is the compile-time equivalent of applying [IniValue(EmptyWhenNull=true)]
        // to every qualifying property.
        if (sectionEmptyWhenNull)
        {
            foreach (var prop in properties)
                if (!prop.IsValueType)
                    prop.EmptyWhenNull = true;
        }

        bool hasAttributeBasedValidation = properties.Any(p => p.HasValidationAttributes);

        return new SectionModel
        {
            Namespace                  = namespaceName,
            InterfaceName              = interfaceName,
            GeneratedClassName         = $"{(interfaceName.StartsWith("I") ? interfaceName.Substring(1) : interfaceName)}Impl",
            SectionName                = sectionName,
            Description                = description,
            ImplementsTransactional    = implementsTransactional,
            ImplementsBeforeSave       = implementsBeforeSave,
            ImplementsAfterSave        = implementsAfterSave,
            ImplementsAfterLoad        = implementsAfterLoad,
            ImplementsAfterLoadGeneric  = implementsAfterLoadGeneric,
            ImplementsBeforeSaveGeneric = implementsBeforeSaveGeneric,
            ImplementsAfterSaveGeneric  = implementsAfterSaveGeneric,
            ImplementsDataValidationGeneric = implementsDataValidationGeneric,
            ImplementsDataValidation    = implementsDataValidation,
            ImplementsUnknownKeyGeneric = implementsUnknownKeyGeneric,
            ImplementsUnknownKey        = implementsUnknownKey,
            HasAttributeBasedValidation = hasAttributeBasedValidation,
            SectionEmptyWhenNull        = sectionEmptyWhenNull,
            SectionIgnoresDefaults      = sectionIgnoresDefaults,
            SectionIgnoresConstants     = sectionIgnoresConstants,
            SectionWriterQuoteValues    = sectionWriterQuoteValues,
            SectionWriterEscapeSequences = sectionWriterEscapeSequences,
            SectionWriterComments       = sectionWriterComments,
            ImplementsINotifyPropertyChanged  = implementsINotifyPropertyChanged,
            ImplementsINotifyPropertyChanging = implementsINotifyPropertyChanging,
            // All properties are included so the generated class satisfies the interface contract.
            // Properties with IsIgnored=true are excluded only from INI read/write operations.
            Properties                 = properties
        };
    }

    private static bool ImplementsInterface(INamedTypeSymbol symbol, string ifaceFqn)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.ToDisplayString() == ifaceFqn)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="symbol"/> implements a generic interface whose
    /// unbound original definition lives in <paramref name="namespaceName"/> and has the
    /// given <paramref name="name"/> (arity&nbsp;1, i.e. <c>IAfterLoad&lt;TSelf&gt;</c>).
    /// </summary>
    private static bool ImplementsGenericInterface(
        INamedTypeSymbol symbol, string namespaceName, string name)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.IsGenericType &&
                iface.Name == name &&
                iface.TypeArguments.Length == 1 &&
                iface.ContainingNamespace.ToDisplayString() == namespaceName)
                return true;
        }
        return false;
    }

    // ── Code emission ─────────────────────────────────────────────────────────

    private static string Emit(SectionModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS8601, CS8604, CS8618, CS8625");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.ComponentModel;");
        sb.AppendLine("using Dapplo.Ini.Configuration;");
        sb.AppendLine("using Dapplo.Ini.Converters;");
        sb.AppendLine();

        bool hasNamespace = !string.IsNullOrEmpty(m.Namespace);
        if (hasNamespace)
        {
            sb.AppendLine($"namespace {m.Namespace}");
            sb.AppendLine("{");
        }

        bool needsNpc = m.ImplementsINotifyPropertyChanged || m.ImplementsINotifyPropertyChanging;
        bool needsValidation = m.ImplementsDataValidationGeneric || m.ImplementsDataValidation
            || m.HasAttributeBasedValidation;

        // Build base class list.
        // When generic lifecycle interfaces are used, the generator also adds the non-generic
        // dispatch interfaces and emits explicit bridge implementations below.
        // Note: INotifyPropertyChanged / INotifyPropertyChanging come from the section interface
        // itself (which is already in the base list), so we do NOT add them to extraBases.
        var extraBases = new System.Collections.Generic.List<string>();
        if (needsValidation)
        {
            extraBases.Add("System.ComponentModel.INotifyDataErrorInfo");
            // IDataValidation is added as a base when we have a validation bridge to emit.
            if (m.ImplementsDataValidationGeneric || m.HasAttributeBasedValidation)
                extraBases.Add("Dapplo.Ini.Interfaces.IDataValidation");
        }
        if (m.ImplementsAfterLoadGeneric)
            extraBases.Add("Dapplo.Ini.Interfaces.IAfterLoad");
        else if (needsValidation && !m.ImplementsAfterLoad)
        {
            // Any form of validation needs to run after load so that errors are populated
            // in _validationErrors immediately (e.g. for WPF settings-screen scenarios).
            // Add IAfterLoad here; the bridge is emitted below.
            extraBases.Add("Dapplo.Ini.Interfaces.IAfterLoad");
        }
        if (m.ImplementsBeforeSaveGeneric)
            extraBases.Add("Dapplo.Ini.Interfaces.IBeforeSave");
        if (m.ImplementsAfterSaveGeneric)
            extraBases.Add("Dapplo.Ini.Interfaces.IAfterSave");
        if (m.ImplementsUnknownKeyGeneric)
            extraBases.Add("Dapplo.Ini.Interfaces.IUnknownKey");

        string baseClasses = "Dapplo.Ini.Configuration.IniSectionBase, " + m.InterfaceName;
        if (extraBases.Count > 0)
            baseClasses += ", " + string.Join(", ", extraBases);

        // Class is partial so consumers can still add code (e.g. helper methods)
        sb.AppendLine($"    public partial class {m.GeneratedClassName} : {baseClasses}");
        sb.AppendLine("    {");

        // ── SectionName ──────────────────────────────────────────────────────
        sb.AppendLine($"        public override string SectionName => \"{EscapeString(m.SectionName)}\";");
        sb.AppendLine();

        // ── NPC events ────────────────────────────────────────────────────────
        if (needsNpc)
        {
            if (m.ImplementsINotifyPropertyChanging)
                sb.AppendLine("        public event PropertyChangingEventHandler? PropertyChanging;");
            if (m.ImplementsINotifyPropertyChanged)
                sb.AppendLine("        public event PropertyChangedEventHandler? PropertyChanged;");
            sb.AppendLine();
        }

        // ── INotifyDataErrorInfo ──────────────────────────────────────────────
        if (needsValidation)
        {
            sb.AppendLine("        private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> _validationErrors");
            sb.AppendLine("            = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(System.StringComparer.Ordinal);");
            sb.AppendLine("        public event System.EventHandler<System.ComponentModel.DataErrorsChangedEventArgs>? ErrorsChanged;");
            sb.AppendLine("        public bool HasErrors => _validationErrors.Count > 0;");
            sb.AppendLine("        public System.Collections.IEnumerable GetErrors(string? propertyName)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (string.IsNullOrEmpty(propertyName))");
            sb.AppendLine("            {");
            sb.AppendLine("                foreach (var kvp in _validationErrors)");
            sb.AppendLine("                    foreach (var err in kvp.Value) yield return err;");
            sb.AppendLine("                yield break;");
            sb.AppendLine("            }");
            sb.AppendLine("            if (_validationErrors.TryGetValue(propertyName, out var list))");
            sb.AppendLine("                foreach (var err in list) yield return err;");
            sb.AppendLine("        }");
            sb.AppendLine("        private void RunValidation(string propertyName)");
            sb.AppendLine("        {");
            sb.AppendLine("            var errors = ValidateProperty(propertyName);");
            sb.AppendLine("            var errorList = new System.Collections.Generic.List<string>(errors);");
            sb.AppendLine("            if (errorList.Count == 0)");
            sb.AppendLine("                _validationErrors.Remove(propertyName);");
            sb.AppendLine("            else");
            sb.AppendLine("                _validationErrors[propertyName] = errorList;");
            sb.AppendLine("            ErrorsChanged?.Invoke(this, new System.ComponentModel.DataErrorsChangedEventArgs(propertyName));");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Emit RunAllValidations — re-validates every non-ignored property and fires
            // ErrorsChanged for each one.  Consumers can call this after opening a
            // settings screen (or any other UI) to ensure all validation errors are visible.
            sb.AppendLine("        public void RunAllValidations()");
            sb.AppendLine("        {");
            foreach (var p in m.Properties.Where(p => !p.IsIgnored))
                sb.AppendLine($"            RunValidation(nameof({p.Name}));");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // ── Transactional state ────────────────────────────────────────────
        if (m.ImplementsTransactional)
        {
            sb.AppendLine("        private bool _isInTransaction;");
            sb.AppendLine("        public bool IsInTransaction => _isInTransaction;");
            sb.AppendLine();
        }

        // ── Backing fields and properties ─────────────────────────────────
        foreach (var p in m.Properties)
        {
            string fieldName = $"_{Camel(p.Name)}";
            string txFieldName = $"_{Camel(p.Name)}Tx";
            bool usesTx = m.ImplementsTransactional && p.IsTransactional;

            // Backing field
            sb.AppendLine($"        private {p.TypeFullName} {fieldName};");
            if (p.IsSubKeyDictionary)
            {
                // Flag that tracks whether any sub-key from the INI file (or a raw-value set) has
                // been received since the last ResetToDefaults call.  The first sub-key received
                // clears the default dictionary before adding the new entry, so file contents
                // fully replace the compiled defaults (consistent with scalar property behaviour).
                sb.AppendLine($"        private bool {fieldName}HasRawEntries;");
            }
            if (usesTx)
                sb.AppendLine($"        private {p.TypeFullName} {txFieldName}; // transaction pending value");

            // Property
            sb.AppendLine($"        public {p.TypeFullName} {p.Name}");
            sb.AppendLine("        {");

            // The getter always starts from the committed backing field.
            // A partial "On{Prop}Get(ref value)" hook can transform the outgoing value.
            // During a transaction the setter only writes to the Tx field,
            // so the backing field still holds the pre-transaction value until Commit().
            sb.AppendLine("            get");
            sb.AppendLine("            {");
            sb.AppendLine($"                var __value = {fieldName};");
            sb.AppendLine($"                On{p.Name}Get(ref __value);");
            sb.AppendLine("                return __value;");
            sb.AppendLine("            }");

            // setter
            sb.AppendLine("            set");
            sb.AppendLine("            {");
            sb.AppendLine("                var __value = value;");
            sb.AppendLine($"                On{p.Name}Set(ref __value);");

            // Determine which events this property should emit.
            // Events are generated at interface level; per-property attributes can suppress them.
            bool emitChanging = m.ImplementsINotifyPropertyChanging && !p.SuppressPropertyChanging;
            bool emitChanged  = m.ImplementsINotifyPropertyChanged  && !p.SuppressPropertyChanged;

            // Emit early-return equality check whenever NPC events will be fired.
            // This prevents redundant events and stops infinite loops in WPF bindings.
            if (emitChanging || emitChanged)
            {
                sb.AppendLine($"                if (EqualityComparer<{p.TypeFullName}>.Default.Equals({fieldName}, __value)) return;");
            }
            if (emitChanging)
            {
                sb.AppendLine($"                PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(nameof({p.Name})));");
            }

            if (p.IsIgnored || p.IsRuntimeOnly)
            {
                // [IgnoreDataMember] / RuntimeOnly — only update the backing field; no INI interaction.
                sb.AppendLine($"                {fieldName} = __value;");
            }
            else
            {
                string keyNameForSet = EscapeString(p.KeyName ?? p.Name);
                if (p.IsSubKeyDictionary)
                {
                    // Sub-key dictionary: emit one SetRawValue call per dictionary entry.
                    // The key in the INI file is "PropertyName.dictionaryKey".
                    if (usesTx)
                    {
                        sb.AppendLine($"                {txFieldName} = __value;");
                        sb.AppendLine($"                if (!_isInTransaction) {{ {fieldName} = __value; {fieldName}HasRawEntries = true; if (__value != null) foreach (var __kvp in __value) SetRawValue($\"{keyNameForSet}.{{__kvp.Key}}\", ConvertToRaw<{p.DictionaryValueTypeFullName}>(__kvp.Value)); }}");
                    }
                    else
                    {
                        sb.AppendLine($"                {fieldName} = __value;");
                        sb.AppendLine($"                {fieldName}HasRawEntries = true;");
                        sb.AppendLine($"                if (__value != null) foreach (var __kvp in __value) SetRawValue($\"{keyNameForSet}.{{__kvp.Key}}\", ConvertToRaw<{p.DictionaryValueTypeFullName}>(__kvp.Value));");
                    }
                }
                else if (usesTx)
                {
                    var convertToRawValue = BuildConvertToRawCall(p, "__value");
                    sb.AppendLine($"                {txFieldName} = __value;");
                    sb.AppendLine($"                if (!_isInTransaction) {{ {fieldName} = __value; SetRawValue(\"{keyNameForSet}\", {convertToRawValue}); }}");
                }
                else
                {
                    var convertToRawValue = BuildConvertToRawCall(p, "__value");
                    sb.AppendLine($"                {fieldName} = __value;");
                    sb.AppendLine($"                SetRawValue(\"{keyNameForSet}\", {convertToRawValue});");
                }
            }

            if (emitChanged)
            {
                sb.AppendLine($"                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof({p.Name})));");
            }
            // Validation always runs in the setter when the section has any form of validation,
            // regardless of whether NPC events are emitted for that property.
            if (needsValidation)
            {
                sb.AppendLine($"                RunValidation(nameof({p.Name}));");
            }
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // ── Per-property partial get/set hooks ─────────────────────────────
        // Consumers can implement these in a separate partial class file to
        // coerce values (set hook) or transform values on read (get hook).
        // If not implemented, calls are removed by the compiler.
        foreach (var p in m.Properties)
        {
            sb.AppendLine($"        partial void On{p.Name}Set(ref {p.TypeFullName} value);");
            sb.AppendLine($"        partial void On{p.Name}Get(ref {p.TypeFullName} value);");
            sb.AppendLine();
        }

        // ── ResetToDefaults ───────────────────────────────────────────────
        sb.AppendLine("        public override void ResetToDefaults()");
        sb.AppendLine("        {");
        foreach (var p in m.Properties)
        {
            // [IgnoreDataMember] properties are not managed by the INI system; leave them untouched.
            if (p.IsIgnored) continue;

            string fieldName = $"_{Camel(p.Name)}";
            if (p.IsSubKeyDictionary)
            {
                // Reset the "file-has-overridden-defaults" flag so the next sub-key load
                // starts fresh (clears the defaults before applying the file entries).
                sb.AppendLine($"            {fieldName}HasRawEntries = false;");
            }
            if (p.DefaultValue != null)
            {
                // Sub-key dictionaries parse their default the same way (inline format for the
                // default string is fine — only the INI file storage uses sub-key notation).
                sb.AppendLine($"            {fieldName} = {BuildConvertFromRawCall(p, $"\"{EscapeString(p.DefaultValue)}\"")};");
            }
            else if (p.EmptyWhenNull)
            {
                // EmptyWhenNull with no DefaultValue: produce an empty instance (e.g. string.Empty,
                // empty List<T>, empty T[], empty Dictionary<K,V>) rather than null/default.
                sb.AppendLine($"            {fieldName} = {BuildConvertFromRawCall(p, "\"\"")};");
            }
            else if (!p.IsValueType)
            {
                // Reference-type property without compile-time EmptyWhenNull: honour the runtime
                // GlobalEmptyWhenNull flag set by IniConfig (from IniConfigBuilder.EmptyWhenNull()).
                sb.AppendLine($"            {fieldName} = {BuildConvertFromRawCall(p, "GlobalEmptyWhenNull ? \"\" : null")};");
            }
            else
            {
                sb.AppendLine($"            {fieldName} = default;");
            }
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        // ── OnRawValueSet ─────────────────────────────────────────────────
        sb.AppendLine("        protected override void OnRawValueSet(string key, string? rawValue)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (key.ToLowerInvariant())");
        sb.AppendLine("            {");
        foreach (var p in m.Properties)
        {
            // [IgnoreDataMember] and RuntimeOnly properties are not loaded from INI.
            if (p.IsIgnored || p.IsRuntimeOnly) continue;

            string keyName = (p.KeyName ?? p.Name).ToLowerInvariant();
            string fieldName = $"_{Camel(p.Name)}";
            if (p.IsSubKeyDictionary)
            {
                // Sub-key pattern: "propertyname.subkey"
                sb.AppendLine($"                case var __sk when __sk.StartsWith(\"{EscapeString(keyName)}.\"):");
                // First sub-key clears the compiled defaults so file data fully replaces them.
                sb.AppendLine($"                    if (!{fieldName}HasRawEntries) {{ {fieldName} = new System.Collections.Generic.Dictionary<string, {p.DictionaryValueTypeFullName}>(System.StringComparer.OrdinalIgnoreCase); {fieldName}HasRawEntries = true; }}");
                sb.AppendLine($"                    if ({fieldName} == null) {fieldName} = new System.Collections.Generic.Dictionary<string, {p.DictionaryValueTypeFullName}>(System.StringComparer.OrdinalIgnoreCase);");
                sb.AppendLine($"                    {fieldName}[key.Substring({keyName.Length + 1})] = ConvertFromRaw<{p.DictionaryValueTypeFullName}>(rawValue);");
                sb.AppendLine("                    break;");
            }
            else
            {
                sb.AppendLine($"                case \"{EscapeString(keyName)}\":");
                string rawArg;
                if (p.EmptyWhenNull)
                    rawArg = "rawValue ?? \"\"";
                else if (!p.IsValueType)
                    // Honour the runtime GlobalEmptyWhenNull flag for reference-type properties
                    // that don't have compile-time EmptyWhenNull.
                    rawArg = "GlobalEmptyWhenNull ? rawValue ?? \"\" : rawValue";
                else
                    rawArg = "rawValue";
                sb.AppendLine($"                    {fieldName} = {BuildConvertFromRawCall(p, rawArg)};");
                sb.AppendLine("                    break;");
            }
        }
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        // ── IsKnownKey ────────────────────────────────────────────────────
        sb.AppendLine("        public override bool IsKnownKey(string key)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (key.ToLowerInvariant())");
        sb.AppendLine("            {");
        foreach (var p in m.Properties)
        {
            // [IgnoreDataMember] and RuntimeOnly properties are not known INI keys.
            if (p.IsIgnored || p.IsRuntimeOnly) continue;

            string keyName = (p.KeyName ?? p.Name).ToLowerInvariant();
            if (p.IsSubKeyDictionary)
                sb.AppendLine($"                case var __k when __k.StartsWith(\"{EscapeString(keyName)}.\"):  return true;");
            else
                sb.AppendLine($"                case \"{EscapeString(keyName)}\": return true;");
        }
        sb.AppendLine("                default: return false;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        // ── GetAllRawValues ───────────────────────────────────────────────
        sb.AppendLine("        public override IEnumerable<KeyValuePair<string, string?>> GetAllRawValues()");
        sb.AppendLine("        {");
        foreach (var p in m.Properties)
        {
            // [IgnoreDataMember], read-only, and RuntimeOnly properties are not serialized to the INI file.
            if (p.IsIgnored || p.IsReadOnly || p.IsRuntimeOnly) continue;
            string fieldName = $"_{Camel(p.Name)}";
            string keyName = p.KeyName ?? p.Name;
            if (p.IsSubKeyDictionary)
            {
                // Yield one entry per key in the dictionary, using "PropertyName.key" as the INI key.
                sb.AppendLine($"            if ({fieldName} != null)");
                sb.AppendLine($"                foreach (var __kvp in {fieldName})");
                sb.AppendLine($"                    yield return new KeyValuePair<string, string?>($\"{EscapeString(keyName)}.{{__kvp.Key}}\", ConvertToRaw<{p.DictionaryValueTypeFullName}>(__kvp.Value));");
            }
            else
            {
                sb.AppendLine($"            yield return new KeyValuePair<string, string?>(\"{EscapeString(keyName)}\", {BuildConvertToRawCall(p, fieldName)});");
            }
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        // ── GetKeys ──────────────────────────────────────────────────────────
        sb.AppendLine("        public override IEnumerable<string> GetKeys()");
        sb.AppendLine("        {");
        foreach (var p in m.Properties)
        {
            // Ignored properties are excluded; read-only and runtime-only are still declared keys.
            if (p.IsIgnored) continue;
            string keyName = p.KeyName ?? p.Name;
            sb.AppendLine($"            yield return \"{EscapeString(keyName)}\";");
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        // ── GetPropertyType ───────────────────────────────────────────────────
        sb.AppendLine("        public override Type? GetPropertyType(string key)");
        sb.AppendLine("        {");
        var metaProps = m.Properties.Where(p => !p.IsIgnored).ToList();
        if (metaProps.Count > 0)
        {
            sb.AppendLine("            switch (key.ToLowerInvariant())");
            sb.AppendLine("            {");
            foreach (var p in metaProps)
            {
                string keyName = (p.KeyName ?? p.Name).ToLowerInvariant();
                // typeof() cannot be used on nullable reference types (CS8639).
                // For value types int? is Nullable<int> and typeof(int?) is valid.
                string typeofArg = (!p.IsValueType && p.TypeFullName.EndsWith("?"))
                    ? p.TypeFullName.Substring(0, p.TypeFullName.Length - 1)
                    : p.TypeFullName;
                if (p.IsSubKeyDictionary)
                    sb.AppendLine($"                case var __pt when __pt.StartsWith(\"{EscapeString(keyName)}.\"):  return typeof({typeofArg});");
                else
                    sb.AppendLine($"                case \"{EscapeString(keyName)}\": return typeof({typeofArg});");
            }
            sb.AppendLine("                default: return null;");
            sb.AppendLine("            }");
        }
        else
        {
            sb.AppendLine("            return null;");
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        // ── GetValueCore ──────────────────────────────────────────────────────
        sb.AppendLine("        protected override object? GetValueCore(string key)");
        sb.AppendLine("        {");
        var valueProps = m.Properties.Where(p => !p.IsIgnored).ToList();
        if (valueProps.Count > 0)
        {
            sb.AppendLine("            switch (key.ToLowerInvariant())");
            sb.AppendLine("            {");
            foreach (var p in valueProps)
            {
                string keyName = (p.KeyName ?? p.Name).ToLowerInvariant();
                string fieldName = $"_{Camel(p.Name)}";
                sb.AppendLine($"                case \"{EscapeString(keyName)}\": return {fieldName};");
            }
            sb.AppendLine("                default: return null;");
            sb.AppendLine("            }");
        }
        else
        {
            sb.AppendLine("            return null;");
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        // ── GetSectionDescription ─────────────────────────────────────────────
        if (m.Description != null)
            sb.AppendLine($"        public override string? GetSectionDescription() => \"{EscapeString(m.Description)}\";");
        else
            sb.AppendLine("        public override string? GetSectionDescription() => null;");
        sb.AppendLine();

        // ── GetPropertyDescription ────────────────────────────────────────────
        sb.AppendLine("        public override string? GetPropertyDescription(string key)");
        sb.AppendLine("        {");
        bool hasPropertyDescriptions = m.Properties.Any(p =>
            !p.IsIgnored && !p.IsReadOnly && !p.IsRuntimeOnly && p.Description != null);
        if (hasPropertyDescriptions)
        {
            sb.AppendLine("            switch (key.ToLowerInvariant())");
            sb.AppendLine("            {");
            foreach (var p in m.Properties)
            {
                if (p.IsIgnored || p.IsReadOnly || p.IsRuntimeOnly || p.Description == null) continue;
                string keyName = (p.KeyName ?? p.Name).ToLowerInvariant();
                if (p.IsSubKeyDictionary)
                    sb.AppendLine($"                case var __pd when __pd.StartsWith(\"{EscapeString(keyName)}.\"):  return \"{EscapeString(p.Description)}\";");
                else
                    sb.AppendLine($"                case \"{EscapeString(keyName)}\": return \"{EscapeString(p.Description)}\";");
            }
            sb.AppendLine("                default: return null;");
            sb.AppendLine("            }");
        }
        else
        {
            sb.AppendLine("            return null;");
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        // ── GetSectionWriterOptions ────────────────────────────────────────────
        bool hasSectionWriterOverrides =
            (m.SectionWriterQuoteValues != null && m.SectionWriterQuoteValues != "Default")
            || (m.SectionWriterEscapeSequences != null && m.SectionWriterEscapeSequences != "Default")
            || (m.SectionWriterComments != null && m.SectionWriterComments != "Default");
        if (hasSectionWriterOverrides)
        {
            sb.AppendLine("        public override Dapplo.Ini.Parsing.IniWriterOptionsOverride? GetSectionWriterOptions()");
            sb.AppendLine("            => new Dapplo.Ini.Parsing.IniWriterOptionsOverride");
            sb.AppendLine("            {");
            if (m.SectionWriterQuoteValues != null && m.SectionWriterQuoteValues != "Default")
                sb.AppendLine($"                QuoteStyle = Dapplo.Ini.Parsing.IniValueQuoteStyle.{m.SectionWriterQuoteValues},");
            if (m.SectionWriterEscapeSequences != null && m.SectionWriterEscapeSequences != "Default")
                sb.AppendLine($"                EscapeSequences = Dapplo.Ini.Parsing.IniBooleanOption.{m.SectionWriterEscapeSequences},");
            if (m.SectionWriterComments != null && m.SectionWriterComments != "Default")
                sb.AppendLine($"                WriteComments = Dapplo.Ini.Parsing.IniBooleanOption.{m.SectionWriterComments},");
            sb.AppendLine("            };");
            sb.AppendLine();
        }

        // ── GetPropertyWriterOptions ───────────────────────────────────────────
        var writerOverrideProps = m.Properties.Where(p =>
            !p.IsIgnored && !p.IsReadOnly && !p.IsRuntimeOnly &&
            ((p.WriterQuoteValues != null && p.WriterQuoteValues != "Default")
             || (p.WriterEscapeSequences != null && p.WriterEscapeSequences != "Default")
             || (p.WriterComments != null && p.WriterComments != "Default"))).ToList();
        if (writerOverrideProps.Count > 0)
        {
            sb.AppendLine("        public override Dapplo.Ini.Parsing.IniWriterOptionsOverride? GetPropertyWriterOptions(string key)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (key.ToLowerInvariant())");
            sb.AppendLine("            {");
            foreach (var p in writerOverrideProps)
            {
                string keyName = (p.KeyName ?? p.Name).ToLowerInvariant();
                var assignments = BuildWriterOverrideAssignments(p.WriterQuoteValues, p.WriterEscapeSequences, p.WriterComments);
                if (p.IsSubKeyDictionary)
                    sb.AppendLine($"                case var __pwo when __pwo.StartsWith(\"{EscapeString(keyName)}.\"): return new Dapplo.Ini.Parsing.IniWriterOptionsOverride {{ {assignments} }};");
                else
                    sb.AppendLine($"                case \"{EscapeString(keyName)}\": return new Dapplo.Ini.Parsing.IniWriterOptionsOverride {{ {assignments} }};");
            }
            sb.AppendLine("                default: return null;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // ── SectionIgnoresDefaults / SectionIgnoresConstants ───────────────────
        if (m.SectionIgnoresDefaults)
        {
            sb.AppendLine("        public override bool SectionIgnoresDefaults => true;");
            sb.AppendLine();
        }
        if (m.SectionIgnoresConstants)
        {
            sb.AppendLine("        public override bool SectionIgnoresConstants => true;");
            sb.AppendLine();
        }

        // ── IsIgnoreDefaultsKey ───────────────────────────────────────────────
        var ignoreDefaultsProps = m.Properties
            .Where(p => !p.IsIgnored && !p.IsRuntimeOnly && p.IgnoreDefaults)
            .ToList();
        if (ignoreDefaultsProps.Count > 0)
        {
            sb.AppendLine("        public override bool IsIgnoreDefaultsKey(string key)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (key.ToLowerInvariant())");
            sb.AppendLine("            {");
            foreach (var p in ignoreDefaultsProps)
            {
                string keyName = (p.KeyName ?? p.Name).ToLowerInvariant();
                if (p.IsSubKeyDictionary)
                    sb.AppendLine($"                case var __idk when __idk.StartsWith(\"{EscapeString(keyName)}.\"):  return true;");
                else
                    sb.AppendLine($"                case \"{EscapeString(keyName)}\": return true;");
            }
            sb.AppendLine("                default: return false;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // ── IsIgnoreConstantsKey ──────────────────────────────────────────────
        var ignoreConstantsProps = m.Properties
            .Where(p => !p.IsIgnored && !p.IsRuntimeOnly && p.IgnoreConstants)
            .ToList();
        if (ignoreConstantsProps.Count > 0)
        {
            sb.AppendLine("        public override bool IsIgnoreConstantsKey(string key)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (key.ToLowerInvariant())");
            sb.AppendLine("            {");
            foreach (var p in ignoreConstantsProps)
            {
                string keyName = (p.KeyName ?? p.Name).ToLowerInvariant();
                if (p.IsSubKeyDictionary)
                    sb.AppendLine($"                case var __ick when __ick.StartsWith(\"{EscapeString(keyName)}.\"):  return true;");
                else
                    sb.AppendLine($"                case \"{EscapeString(keyName)}\": return true;");
            }
            sb.AppendLine("                default: return false;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        if (m.ImplementsTransactional)
        {
            var txProps = m.Properties.Where(p => p.IsTransactional).ToList();

            sb.AppendLine("        public void Begin()");
            sb.AppendLine("        {");
            sb.AppendLine("            if (_isInTransaction) return;");
            sb.AppendLine("            _isInTransaction = true;");
            // Snapshot current values into Tx fields
            foreach (var p in txProps)
                sb.AppendLine($"            _{Camel(p.Name)}Tx = _{Camel(p.Name)};");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        public void Commit()");
            sb.AppendLine("        {");
            sb.AppendLine("            if (!_isInTransaction) return;");
            sb.AppendLine("            _isInTransaction = false;");
            foreach (var p in txProps)
                sb.AppendLine($"            _{Camel(p.Name)} = _{Camel(p.Name)}Tx;");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        public void Rollback()");
            sb.AppendLine("        {");
            sb.AppendLine("            if (!_isInTransaction) return;");
            sb.AppendLine("            _isInTransaction = false;");
            // Discard Tx changes - old values remain in backing fields
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // ── Generic lifecycle hook bridges ────────────────────────────────
        // When a consumer uses IAfterLoad<TSelf> / IBeforeSave<TSelf> / IAfterSave<TSelf>
        // the generator emits explicit implementations of the non-generic dispatch interfaces
        // that delegate to the static virtual method on the consumer's interface.
        string ifaceFqn = string.IsNullOrEmpty(m.Namespace)
            ? m.InterfaceName
            : $"{m.Namespace}.{m.InterfaceName}";

        if (m.ImplementsAfterLoadGeneric)
        {
            // When any validation is also present, run it alongside the consumer hook.
            if (needsValidation)
            {
                sb.AppendLine("        void Dapplo.Ini.Interfaces.IAfterLoad.OnAfterLoad()");
                sb.AppendLine("        {");
                sb.AppendLine($"            {ifaceFqn}.OnAfterLoad(this);");
                sb.AppendLine("            RunAllValidations();");
                sb.AppendLine("        }");
            }
            else
            {
                sb.AppendLine("        void Dapplo.Ini.Interfaces.IAfterLoad.OnAfterLoad()");
                sb.AppendLine($"            => {ifaceFqn}.OnAfterLoad(this);");
            }
            sb.AppendLine();
        }
        else if (needsValidation && !m.ImplementsAfterLoad)
        {
            // No consumer IAfterLoad hook: emit our own bridge to run all validation after load.
            sb.AppendLine("        void Dapplo.Ini.Interfaces.IAfterLoad.OnAfterLoad()");
            sb.AppendLine("            => RunAllValidations();");
            sb.AppendLine();
        }
        // When m.ImplementsAfterLoad (non-generic) AND needsValidation:
        // The consumer implements OnAfterLoad() in a partial class; we expose RunAllValidations()
        // as a protected helper they can call explicitly.

        if (m.ImplementsBeforeSaveGeneric)
        {
            sb.AppendLine("        bool Dapplo.Ini.Interfaces.IBeforeSave.OnBeforeSave()");
            sb.AppendLine($"            => {ifaceFqn}.OnBeforeSave(this);");
            sb.AppendLine();
        }

        if (m.ImplementsAfterSaveGeneric)
        {
            sb.AppendLine("        void Dapplo.Ini.Interfaces.IAfterSave.OnAfterSave()");
            sb.AppendLine($"            => {ifaceFqn}.OnAfterSave(this);");
            sb.AppendLine();
        }

        // ── IUnknownKey bridge ────────────────────────────────────────────────
        // When the generic IUnknownKey<TSelf> is used, emit a bridge so the framework
        // can call it through the non-generic IUnknownKey dispatch interface.
        if (m.ImplementsUnknownKeyGeneric)
        {
            sb.AppendLine("        void Dapplo.Ini.Interfaces.IUnknownKey.OnUnknownKey(string key, string? value)");
            sb.AppendLine($"            => {ifaceFqn}.OnUnknownKey(this, key, value);");
            sb.AppendLine();
        }

        // ── IDataValidation bridges and attribute-based ValidateProperty ──────
        // The private ValidateProperty(string) helper is used by RunValidation().
        // When attribute-based validation is active it includes the DataAnnotations rules.
        // When IDataValidation<TSelf> or IDataValidation is also present, both rule sets
        // are merged so the consumer's custom rules are honoured as well.
        if (m.HasAttributeBasedValidation)
        {
            // Emit ValidateAttributeRules — the generated per-property checks.
            sb.AppendLine("        private System.Collections.Generic.IEnumerable<string> ValidateAttributeRules(string propertyName)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (propertyName)");
            sb.AppendLine("            {");
            foreach (var p in m.Properties)
            {
                if (!p.HasValidationAttributes) continue;
                string fieldName = $"_{Camel(p.Name)}";
                sb.AppendLine($"                case nameof({p.Name}):");
                bool isStringType = p.TypeFullName == "string" || p.TypeFullName == "string?";
                // A non-nullable value type (int, bool, struct) can never be null at runtime,
                // so [Required] is always satisfied — we skip the check entirely.
                // Nullable value types (int?, bool?) end with '?' and still need a null check.
                bool isNonNullableValueType = p.IsValueType && !p.TypeFullName.EndsWith("?");
                if (p.IsRequired)
                {
                    string requiredMsg = EscapeString(p.RequiredErrorMessage ?? $"{p.Name} is required.");
                    if (isStringType)
                        sb.AppendLine($"                    if (string.IsNullOrEmpty({fieldName})) yield return \"{requiredMsg}\";");
                    else if (!isNonNullableValueType)
                        // Covers nullable value types (int?, etc.) and reference types (string already handled above)
                        sb.AppendLine($"                    if ({fieldName} == null) yield return \"{requiredMsg}\";");
                    // Non-nullable value types are always satisfied; skip.
                }
                if (p.RangeMinRaw != null && p.RangeMaxRaw != null)
                {
                    string rangeMsg = EscapeString(p.RangeErrorMessage ?? $"{p.Name} must be between {p.RangeMinRaw} and {p.RangeMaxRaw}.");
                    // Use IComparable for generic range check to support int, double, etc.
                    sb.AppendLine($"                    {{ var __cv = (System.IComparable){fieldName}; if (__cv.CompareTo({p.RangeMinRaw}) < 0 || __cv.CompareTo({p.RangeMaxRaw}) > 0) yield return \"{rangeMsg}\"; }}");
                }
                if (p.MaxLength.HasValue)
                {
                    string maxLenMsg = EscapeString(p.MaxLengthErrorMessage ?? $"{p.Name} must not exceed {p.MaxLength.Value} characters.");
                    sb.AppendLine($"                    if ({fieldName} != null && {fieldName}.Length > {p.MaxLength.Value}) yield return \"{maxLenMsg}\";");
                }
                if (p.RegexPattern != null)
                {
                    string regexMsg = EscapeString(p.RegexErrorMessage ?? $"{p.Name} does not match the required pattern.");
                    string escapedPattern = EscapeString(p.RegexPattern);
                    sb.AppendLine($"                    if ({fieldName} != null && !System.Text.RegularExpressions.Regex.IsMatch({fieldName}, \"{escapedPattern}\")) yield return \"{regexMsg}\";");
                }
                sb.AppendLine("                    break;");
            }
            sb.AppendLine("                default: break;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Emit RunAllAttributeValidations — calls RunValidation for every validated property.
            var validatedPropNames = m.Properties.Where(p => p.HasValidationAttributes)
                .Select(p => p.Name).ToList();
            sb.AppendLine("        protected void RunAllAttributeValidations()");
            sb.AppendLine("        {");
            foreach (var propName in validatedPropNames)
                sb.AppendLine($"            RunValidation(nameof({propName}));");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Emit explicit IDataValidation.ValidateProperty bridge.
            sb.AppendLine("        System.Collections.Generic.IEnumerable<string> Dapplo.Ini.Interfaces.IDataValidation.ValidateProperty(string propertyName)");
            sb.AppendLine("            => ValidateProperty(propertyName);");
            sb.AppendLine();

            // Emit the private ValidateProperty helper, merging attribute rules with any consumer rules.
            sb.AppendLine("        private System.Collections.Generic.IEnumerable<string> ValidateProperty(string propertyName)");
            sb.AppendLine("        {");
            sb.AppendLine("            foreach (var __e in ValidateAttributeRules(propertyName)) yield return __e;");
            if (m.ImplementsDataValidationGeneric)
                sb.AppendLine($"            foreach (var __e in {ifaceFqn}.ValidateProperty(this, propertyName)) yield return __e;");
            else if (m.ImplementsDataValidation)
                sb.AppendLine("            foreach (var __e in ((Dapplo.Ini.Interfaces.IDataValidation)this).ValidateProperty(propertyName)) yield return __e;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        else if (m.ImplementsDataValidationGeneric)
        {
            sb.AppendLine("        System.Collections.Generic.IEnumerable<string> Dapplo.Ini.Interfaces.IDataValidation.ValidateProperty(string propertyName)");
            sb.AppendLine($"            => {ifaceFqn}.ValidateProperty(this, propertyName);");
            sb.AppendLine();
            // Provide the internal ValidateProperty helper used by RunValidation()
            sb.AppendLine("        private System.Collections.Generic.IEnumerable<string> ValidateProperty(string propertyName)");
            sb.AppendLine($"            => {ifaceFqn}.ValidateProperty(this, propertyName);");
            sb.AppendLine();
        }
        else if (m.ImplementsDataValidation)
        {
            // Non-generic: consumer implements ValidateProperty(string) in a partial class.
            // Wire RunValidation() to it.
            sb.AppendLine("        private System.Collections.Generic.IEnumerable<string> ValidateProperty(string propertyName)");
            sb.AppendLine("            => ((Dapplo.Ini.Interfaces.IDataValidation)this).ValidateProperty(propertyName);");
            sb.AppendLine();
        }

        sb.AppendLine("    }"); // end class

        if (hasNamespace)
            sb.AppendLine("}"); // end namespace

        return sb.ToString();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string Camel(string name)
        => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static string EscapeString(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

    private static string EscapeCharLiteral(char c)
        => c switch
        {
            '\'' => "\\'",
            '\\' => "\\\\",
            '\0' => "\\0",
            '\a' => "\\a",
            '\b' => "\\b",
            '\f' => "\\f",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            '\v' => "\\v",
            _ => c.ToString()
        };

    private static string BuildConvertFromRawCall(PropertyModel p, string rawExpression)
    {
        if (p.IsListLike)
            return $"ConvertFromRaw<{p.TypeFullName}>({rawExpression}, '{EscapeCharLiteral(p.ListDelimiter)}')";
        return $"ConvertFromRaw<{p.TypeFullName}>({rawExpression})";
    }

    private static string BuildConvertToRawCall(PropertyModel p, string valueExpression)
    {
        if (p.IsListLike)
            return $"ConvertToRaw({valueExpression}, '{EscapeCharLiteral(p.ListDelimiter)}')";
        return $"ConvertToRaw({valueExpression})";
    }

    private static string BuildWriterOverrideAssignments(string? quoteValues, string? escapeSequences, string? writeComments)
    {
        var parts = new List<string>();
        if (quoteValues != null && quoteValues != "Default")
            parts.Add($"QuoteStyle = Dapplo.Ini.Parsing.IniValueQuoteStyle.{quoteValues}");
        if (escapeSequences != null && escapeSequences != "Default")
            parts.Add($"EscapeSequences = Dapplo.Ini.Parsing.IniBooleanOption.{escapeSequences}");
        if (writeComments != null && writeComments != "Default")
            parts.Add($"WriteComments = Dapplo.Ini.Parsing.IniBooleanOption.{writeComments}");
        return string.Join(", ", parts);
    }

    private static string? GetEnumValueName(TypedConstant constant)
    {
        if (constant.Value == null)
            return null;

        if (constant.Type is not INamedTypeSymbol enumType || enumType.TypeKind != TypeKind.Enum)
            return constant.Value.ToString();

        var value = System.Convert.ToInt64(constant.Value);
        foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
        {
            if (!member.HasConstantValue || member.ConstantValue == null)
                continue;

            if (System.Convert.ToInt64(member.ConstantValue) == value)
                return member.Name;
        }
        return constant.Value.ToString();
    }

    /// <summary>
    /// Formats the <paramref name="value"/> from a <c>[DefaultValue(...)]</c> constructor argument
    /// into the string representation understood by the registered converters (invariant culture).
    /// </summary>
    private static string FormatDefaultValueAsString(object value)
    {
        return value switch
        {
            bool b   => b ? "True" : "False",
            double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            float f  => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            decimal dc => dc.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _        => System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""
        };
    }

    /// <summary>
    /// Formats a <c>[Range]</c> constructor argument (<see cref="TypedConstant"/>) as an inline C# literal
    /// suitable for use in a comparison expression.
    /// </summary>
    private static string FormatRangeArgAsLiteral(TypedConstant arg)
    {
        if (arg.Value == null) return "null";
        return arg.Value switch
        {
            int i    => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long l   => $"{l.ToString(System.Globalization.CultureInfo.InvariantCulture)}L",
            double d => $"{d.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}d",
            float f  => $"{f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}f",
            decimal dc => $"{dc.ToString(System.Globalization.CultureInfo.InvariantCulture)}m",
            string s => $"\"{EscapeString(s)}\"",
            _        => System.Convert.ToString(arg.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "0"
        };
    }
}
