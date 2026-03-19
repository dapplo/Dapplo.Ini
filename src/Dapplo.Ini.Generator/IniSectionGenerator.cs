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
/// Incremental source generator that creates a concrete class for every interface
/// annotated with <c>[IniSection]</c>.
/// </summary>
[Generator]
public sealed class IniSectionGenerator : IIncrementalGenerator
{
    private const string IniSectionAttributeFqn = "Dapplo.Ini.Attributes.IniSectionAttribute";
    private const string IniValueAttributeFqn   = "Dapplo.Ini.Attributes.IniValueAttribute";

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
        // Filter for interface declarations that carry [IniSection]
        var interfaces = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InterfaceDeclarationSyntax ids
                                               && ids.AttributeLists.Count > 0,
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
        public bool NotifyPropertyChanged { get; set; }
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
        public List<PropertyModel> Properties { get; set; } = new();
    }

    // ── Extraction ────────────────────────────────────────────────────────────

    private static SectionModel? GetInterfaceModel(GeneratorSyntaxContext ctx)
    {
        var ids = (InterfaceDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(ids) as INamedTypeSymbol;
        if (symbol is null) return null;

        // Must have [IniSection]
        var iniSectionAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == IniSectionAttributeFqn);
        if (iniSectionAttr is null) return null;

        var interfaceName = symbol.Name;
        var namespaceName = symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();

        // Determine section name: [IniSection] arg → [DataContract] Name → strip leading 'I' → use interface name
        string sectionName;
        if (iniSectionAttr.ConstructorArguments.Length > 0 &&
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
        foreach (var na in iniSectionAttr.NamedArguments)
        {
            if (na.Key == "Description" && na.Value.Value is string d)
                description = d;
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
                        case "KeyName":               prop.KeyName = na.Value.Value as string; break;
                        case "DefaultValue":          prop.DefaultValue = na.Value.Value as string; break;
                        case "Description":           prop.Description = na.Value.Value as string; break;
                        case "Transactional":         prop.IsTransactional = na.Value.Value is true; break;
                        case "NotifyPropertyChanged": prop.NotifyPropertyChanged = na.Value.Value is true; break;
                        case "ReadOnly":              prop.IsReadOnly = na.Value.Value is true; break;
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

        bool needsNpc = m.Properties.Any(p => p.NotifyPropertyChanged);
        bool needsValidation = m.ImplementsDataValidationGeneric || m.ImplementsDataValidation
            || m.HasAttributeBasedValidation;

        // Build base class list.
        // When generic lifecycle interfaces are used, the generator also adds the non-generic
        // dispatch interfaces and emits explicit bridge implementations below.
        var extraBases = new System.Collections.Generic.List<string>();
        if (needsNpc)
        {
            extraBases.Add("INotifyPropertyChanging");
            extraBases.Add("INotifyPropertyChanged");
        }
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
            sb.AppendLine("        public event PropertyChangingEventHandler? PropertyChanging;");
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

            // The getter always returns the committed backing field.
            // During a transaction the setter only writes to the Tx field,
            // so the backing field still holds the pre-transaction value until Commit().
            sb.AppendLine($"            get => {fieldName};");

            // setter
            sb.AppendLine("            set");
            sb.AppendLine("            {");
            if (p.NotifyPropertyChanged)
            {
                sb.AppendLine($"                if (EqualityComparer<{p.TypeFullName}>.Default.Equals({fieldName}, value)) return;");
                sb.AppendLine($"                PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(nameof({p.Name})));");
            }

            if (p.IsIgnored)
            {
                // [IgnoreDataMember] — only update the backing field; no INI interaction.
                sb.AppendLine($"                {fieldName} = value;");
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
                        sb.AppendLine($"                {txFieldName} = value;");
                        sb.AppendLine($"                if (!_isInTransaction) {{ {fieldName} = value; {fieldName}HasRawEntries = true; if (value != null) foreach (var __kvp in value) SetRawValue($\"{keyNameForSet}.{{__kvp.Key}}\", ConvertToRaw<{p.DictionaryValueTypeFullName}>(__kvp.Value)); }}");
                    }
                    else
                    {
                        sb.AppendLine($"                {fieldName} = value;");
                        sb.AppendLine($"                {fieldName}HasRawEntries = true;");
                        sb.AppendLine($"                if (value != null) foreach (var __kvp in value) SetRawValue($\"{keyNameForSet}.{{__kvp.Key}}\", ConvertToRaw<{p.DictionaryValueTypeFullName}>(__kvp.Value));");
                    }
                }
                else if (usesTx)
                {
                    sb.AppendLine($"                {txFieldName} = value;");
                    sb.AppendLine($"                if (!_isInTransaction) {{ {fieldName} = value; SetRawValue(\"{keyNameForSet}\", ConvertToRaw(value)); }}");
                }
                else
                {
                    sb.AppendLine($"                {fieldName} = value;");
                    sb.AppendLine($"                SetRawValue(\"{keyNameForSet}\", ConvertToRaw(value));");
                }
            }

            if (p.NotifyPropertyChanged)
            {
                sb.AppendLine($"                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof({p.Name})));");
            }
            if (needsValidation && (p.NotifyPropertyChanged || p.HasValidationAttributes))
            {
                sb.AppendLine($"                RunValidation(nameof({p.Name}));");
            }
            sb.AppendLine("            }");
            sb.AppendLine("        }");
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
                sb.AppendLine($"            {fieldName} = ConvertFromRaw<{p.TypeFullName}>(\"{EscapeString(p.DefaultValue)}\");");
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
            // [IgnoreDataMember] properties are not loaded from INI.
            if (p.IsIgnored) continue;

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
                sb.AppendLine($"                    {fieldName} = ConvertFromRaw<{p.TypeFullName}>(rawValue);");
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
            // [IgnoreDataMember] properties are not known INI keys.
            if (p.IsIgnored) continue;

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
            // [IgnoreDataMember] and read-only properties are not serialized to the INI file.
            if (p.IsIgnored || p.IsReadOnly) continue;
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
                sb.AppendLine($"            yield return new KeyValuePair<string, string?>(\"{EscapeString(keyName)}\", ConvertToRaw({fieldName}));");
            }
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        // ── Transactional implementation ──────────────────────────────────
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
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

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
