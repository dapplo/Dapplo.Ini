// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Dapplo.Ini.Config.Generator;

/// <summary>
/// Incremental source generator that creates a concrete class for every interface
/// annotated with <c>[IniSection]</c>.
/// </summary>
[Generator]
public sealed class IniSectionGenerator : IIncrementalGenerator
{
    private const string IniSectionAttributeFqn = "Dapplo.Ini.Config.Attributes.IniSectionAttribute";
    private const string IniValueAttributeFqn   = "Dapplo.Ini.Config.Attributes.IniValueAttribute";

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
        public bool ImplementsBeforeSave { get; set; }
        public bool ImplementsAfterSave { get; set; }
        public bool ImplementsAfterLoad { get; set; }
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
        bool implementsTransactional = ImplementsInterface(symbol, "Dapplo.Ini.Config.Interfaces.ITransactional");
        bool implementsBeforeSave    = ImplementsInterface(symbol, "Dapplo.Ini.Config.Interfaces.IBeforeSave");
        bool implementsAfterSave     = ImplementsInterface(symbol, "Dapplo.Ini.Config.Interfaces.IAfterSave");
        bool implementsAfterLoad     = ImplementsInterface(symbol, "Dapplo.Ini.Config.Interfaces.IAfterLoad");

        var properties = new List<PropertyModel>();
        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            var prop = new PropertyModel
            {
                Name = member.Name,
                TypeFullName = member.Type.ToDisplayString(),
                IsValueType  = member.Type.IsValueType
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
            Namespace              = namespaceName,
            InterfaceName          = interfaceName,
            GeneratedClassName     = $"{(interfaceName.StartsWith("I") ? interfaceName.Substring(1) : interfaceName)}Impl",
            SectionName            = sectionName,
            Description            = description,
            ImplementsTransactional = implementsTransactional,
            ImplementsBeforeSave   = implementsBeforeSave,
            ImplementsAfterSave    = implementsAfterSave,
            ImplementsAfterLoad    = implementsAfterLoad,
            Properties             = properties
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
        sb.AppendLine("using Dapplo.Ini.Config.Configuration;");
        sb.AppendLine("using Dapplo.Ini.Config.Converters;");
        sb.AppendLine();

        bool hasNamespace = !string.IsNullOrEmpty(m.Namespace);
        if (hasNamespace)
        {
            sb.AppendLine($"namespace {m.Namespace}");
            sb.AppendLine("{");
        }

        bool needsNpc = m.Properties.Any(p => p.NotifyPropertyChanged);

        // Class declaration – partial so consumers can add lifecycle implementations
        string baseClasses = "Dapplo.Ini.Config.Configuration.IniSectionBase, " + m.InterfaceName;
        if (needsNpc)
            baseClasses += ", INotifyPropertyChanging, INotifyPropertyChanged";

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
