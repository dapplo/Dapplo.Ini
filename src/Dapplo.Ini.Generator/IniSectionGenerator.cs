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

    // FQNs used for non-generic (dispatch) lifecycle interfaces
    private const string IAfterLoadFqn        = "Dapplo.Ini.Interfaces.IAfterLoad";
    private const string IBeforeSaveFqn       = "Dapplo.Ini.Interfaces.IBeforeSave";
    private const string IAfterSaveFqn        = "Dapplo.Ini.Interfaces.IAfterSave";
    private const string ITransactionalFqn    = "Dapplo.Ini.Interfaces.ITransactional";
    private const string IDataValidationFqn   = "Dapplo.Ini.Interfaces.IDataValidation";

    // Names of the generic (static-virtual) lifecycle interfaces
    private const string IAfterLoadGenericName      = "IAfterLoad";
    private const string IBeforeSaveGenericName     = "IBeforeSave";
    private const string IAfterSaveGenericName      = "IAfterSave";
    private const string IDataValidationGenericName = "IDataValidation";
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

        // Determine section name: attribute arg → strip leading 'I' → use interface name
        string sectionName;
        if (iniSectionAttr.ConstructorArguments.Length > 0 &&
            iniSectionAttr.ConstructorArguments[0].Value is string sn && !string.IsNullOrEmpty(sn))
            sectionName = sn;
        else if (interfaceName.StartsWith("I") && interfaceName.Length > 1)
            sectionName = interfaceName.Substring(1);
        else
            sectionName = interfaceName;

        string? description = null;
        foreach (var na in iniSectionAttr.NamedArguments)
        {
            if (na.Key == "Description" && na.Value.Value is string d)
                description = d;
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

        var properties = new List<PropertyModel>();
        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            var prop = new PropertyModel
            {
                Name = member.Name,
                TypeFullName = member.Type.ToDisplayString(),
                IsValueType  = member.Type.IsValueType,
                // A getter-only interface property ({ get; }) is treated as read-only:
                // the value is loaded from the INI file and defaults are applied, but
                // it is never written back to disk.  The generated implementation still
                // exposes a public setter so the framework (and callers with access to
                // the concrete class) can assign values; the setter is simply not part
                // of the interface contract.
                IsReadOnly   = member.SetMethod == null
            };

            // Collect [IniValue] attribute
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

            properties.Add(prop);
        }

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
        bool needsValidation = m.ImplementsDataValidationGeneric || m.ImplementsDataValidation;

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
            if (m.ImplementsDataValidationGeneric)
                extraBases.Add("Dapplo.Ini.Interfaces.IDataValidation");
        }
        if (m.ImplementsAfterLoadGeneric)
            extraBases.Add("Dapplo.Ini.Interfaces.IAfterLoad");
        if (m.ImplementsBeforeSaveGeneric)
            extraBases.Add("Dapplo.Ini.Interfaces.IBeforeSave");
        if (m.ImplementsAfterSaveGeneric)
            extraBases.Add("Dapplo.Ini.Interfaces.IAfterSave");

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
            if (usesTx)
            {
                sb.AppendLine($"                {txFieldName} = value;");
                sb.AppendLine($"                if (!_isInTransaction) {{ {fieldName} = value; SetRawValue(\"{EscapeString(p.KeyName ?? p.Name)}\", ConvertToRaw(value)); }}");
            }
            else
            {
                sb.AppendLine($"                {fieldName} = value;");
                sb.AppendLine($"                SetRawValue(\"{EscapeString(p.KeyName ?? p.Name)}\", ConvertToRaw(value));");
            }
            if (p.NotifyPropertyChanged)
            {
                sb.AppendLine($"                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof({p.Name})));");
                if (needsValidation)
                {
                    sb.AppendLine($"                RunValidation(nameof({p.Name}));");
                }
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
            string fieldName = $"_{Camel(p.Name)}";
            if (p.DefaultValue != null)
            {
                // Store default as raw and let converter parse it
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
            string keyName = (p.KeyName ?? p.Name).ToLowerInvariant();
            string fieldName = $"_{Camel(p.Name)}";
            sb.AppendLine($"                case \"{EscapeString(keyName)}\":");
            sb.AppendLine($"                    {fieldName} = ConvertFromRaw<{p.TypeFullName}>(rawValue);");
            sb.AppendLine("                    break;");
        }
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        // ── GetAllRawValues ───────────────────────────────────────────────
        sb.AppendLine("        public override IEnumerable<KeyValuePair<string, string?>> GetAllRawValues()");
        sb.AppendLine("        {");
        foreach (var p in m.Properties)
        {
            if (p.IsReadOnly) continue;
            string fieldName = $"_{Camel(p.Name)}";
            string keyName = p.KeyName ?? p.Name;
            sb.AppendLine($"            yield return new KeyValuePair<string, string?>(\"{EscapeString(keyName)}\", ConvertToRaw({fieldName}));");
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
            sb.AppendLine("        void Dapplo.Ini.Interfaces.IAfterLoad.OnAfterLoad()");
            sb.AppendLine($"            => {ifaceFqn}.OnAfterLoad(this);");
            sb.AppendLine();
        }

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

        // ── IDataValidation bridges ───────────────────────────────────────────
        // When the generic IDataValidation<TSelf> is used, emit a bridge so the framework
        // can call it through the non-generic IDataValidation dispatch interface and wire
        // it to RunValidation() which feeds INotifyDataErrorInfo.
        if (m.ImplementsDataValidationGeneric)
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
}
