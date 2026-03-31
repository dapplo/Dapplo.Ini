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
/// annotated with <c>[IniLanguageSection]</c>.
/// </summary>
[Generator]
public sealed class IniLanguageSectionGenerator : IIncrementalGenerator
{
    private const string IniLanguageSectionAttributeFqn =
        "Dapplo.Ini.Internationalization.Attributes.IniLanguageSectionAttribute";

    private const string ILanguageSectionFqn =
        "Dapplo.Ini.Internationalization.Interfaces.ILanguageSection";

    private const string IReadOnlyDictionaryOpenFqn =
        "System.Collections.Generic.IReadOnlyDictionary<string, string>";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var interfaces = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InterfaceDeclarationSyntax ids
                                               && ids.AttributeLists.Count > 0,
                transform: static (ctx, _) => GetModel(ctx))
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
        /// <summary>Normalized key: lowercase, underscores/dashes removed.</summary>
        public string NormalizedKey { get; set; } = "";
    }

    private sealed class LanguageSectionModel
    {
        public string Namespace { get; set; } = "";
        public string InterfaceName { get; set; } = "";
        public string GeneratedClassName { get; set; } = "";
        /// <summary>The [SectionName] header used in the ini file. Always non-null (derived from interface name if not explicit).</summary>
        public string SectionName { get; set; } = "";
        /// <summary>Optional module name for file naming: {basename}.{moduleName}.{ietf}.ini.</summary>
        public string? ModuleName { get; set; }
        /// <summary>True when the interface also extends IReadOnlyDictionary&lt;string,string&gt;.</summary>
        public bool ImplementsReadOnlyDictionary { get; set; }
        public List<PropertyModel> Properties { get; set; } = new();
    }

    // ── Extraction ────────────────────────────────────────────────────────────

    private static LanguageSectionModel? GetModel(GeneratorSyntaxContext ctx)
    {
        var ids = (InterfaceDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(ids) as INamedTypeSymbol;
        if (symbol is null) return null;

        // Must have [IniLanguageSection]
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == IniLanguageSectionAttributeFqn);
        if (attr is null) return null;

        var interfaceName = symbol.Name;
        var namespaceName = symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();

        // Read optional SectionName from attribute constructor argument (first positional arg)
        string? explicitSectionName = null;
        if (attr.ConstructorArguments.Length > 0 &&
            attr.ConstructorArguments[0].Value is string sn &&
            !string.IsNullOrEmpty(sn))
            explicitSectionName = sn;

        // Also check named argument for SectionName
        foreach (var na in attr.NamedArguments)
            if (na.Key == "SectionName" && na.Value.Value is string snNamed)
                explicitSectionName = snNamed;

        // Derive section name from interface name when not explicitly provided
        // (strip leading 'I' prefix, e.g. IMainLanguage → MainLanguage)
        var derivedSectionName = interfaceName.Length > 1 && interfaceName[0] == 'I'
            ? interfaceName.Substring(1)
            : interfaceName;
        var sectionName = explicitSectionName ?? derivedSectionName;

        // Read optional ModuleName from named attribute argument (controls file naming)
        string? moduleName = null;
        foreach (var na in attr.NamedArguments)
            if (na.Key == "ModuleName" && na.Value.Value is string mnNamed)
                moduleName = mnNamed;

        // Check whether the interface extends IReadOnlyDictionary<string, string>
        bool implementsReadOnlyDictionary = symbol.AllInterfaces.Any(i =>
            i.ToDisplayString() == IReadOnlyDictionaryOpenFqn);

        // Collect string-typed get-only properties from the interface itself and
        // all its base interfaces (excluding ILanguageSection and system interfaces).
        var properties = CollectProperties(symbol);

        return new LanguageSectionModel
        {
            Namespace                 = namespaceName,
            InterfaceName             = interfaceName,
            GeneratedClassName        = $"{(interfaceName.StartsWith("I") && interfaceName.Length > 1 ? interfaceName.Substring(1) : interfaceName)}Impl",
            SectionName               = sectionName,
            ModuleName                = moduleName,
            ImplementsReadOnlyDictionary = implementsReadOnlyDictionary,
            Properties                = properties
        };
    }

    private static List<PropertyModel> CollectProperties(INamedTypeSymbol symbol)
    {
        var result = new List<PropertyModel>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        // Walk the interface hierarchy (current interface first, then base interfaces)
        var toVisit = new Queue<INamedTypeSymbol>();
        toVisit.Enqueue(symbol);

        while (toVisit.Count > 0)
        {
            var current = toVisit.Dequeue();

            // Skip system and framework interfaces
            var ns = current.ContainingNamespace?.ToDisplayString() ?? "";
            if (ns.StartsWith("System") || current.ToDisplayString() == ILanguageSectionFqn)
                continue;
            if (current.ToDisplayString() == IReadOnlyDictionaryOpenFqn)
                continue;

            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                // Language section properties must be string get-only
                if (member.SetMethod != null) continue;
                if (member.Type.SpecialType != SpecialType.System_String) continue;
                if (!seen.Add(member.Name)) continue;

                result.Add(new PropertyModel
                {
                    Name          = member.Name,
                    NormalizedKey = NormalizeKey(member.Name)
                });
            }

            foreach (var iface in current.Interfaces)
                toVisit.Enqueue(iface);
        }

        return result;
    }

    /// <summary>
    /// Normalises a property name to the language pack lookup key:
    /// remove underscores and dashes, convert to lower-case.
    /// </summary>
    private static string NormalizeKey(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (ch != '_' && ch != '-')
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    // ── Code emission ─────────────────────────────────────────────────────────

    private static string Emit(LanguageSectionModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS8601, CS8604, CS8618, CS8625");
        sb.AppendLine();
        sb.AppendLine("using Dapplo.Ini.Internationalization.Configuration;");
        sb.AppendLine();

        bool hasNamespace = !string.IsNullOrEmpty(m.Namespace);
        if (hasNamespace)
        {
            sb.AppendLine($"namespace {m.Namespace}");
            sb.AppendLine("{");
        }

        string baseClasses = $"Dapplo.Ini.Internationalization.Configuration.LanguageSectionBase, {m.InterfaceName}";

        sb.AppendLine($"    public sealed partial class {m.GeneratedClassName} : {baseClasses}");
        sb.AppendLine("    {");

        // SectionName override (always non-null: explicit or derived from interface name)
        sb.AppendLine($"        public override string SectionName => \"{EscapeString(m.SectionName)}\";");

        // ModuleName override (null when no module file is needed)
        var moduleExpr = m.ModuleName != null
            ? $"\"{EscapeString(m.ModuleName)}\""
            : "null";
        sb.AppendLine($"        public override string? ModuleName => {moduleExpr};");
        sb.AppendLine();

        // Generate one property per string property declared on the interface
        foreach (var p in m.Properties)
        {
            sb.AppendLine($"        public string {p.Name} => GetTranslation(\"{EscapeString(p.NormalizedKey)}\", nameof({p.Name}));");
        }

        sb.AppendLine("    }");

        if (hasNamespace)
            sb.AppendLine("}");

        return sb.ToString();
    }

    private static string EscapeString(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
