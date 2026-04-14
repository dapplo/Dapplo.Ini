// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Dapplo.Ini.Ui.Generator;

/// <summary>
/// Incremental source generator that creates a compile-time <c>{TypeName}UiDescriptor</c>
/// static class for every <c>IIniSection</c>-implementing interface that carries at least
/// one UI attribute from <c>Dapplo.Ini.Ui.Attributes</c>.
/// </summary>
/// <remarks>
/// <para>
/// The generated class contains a single <c>Page</c> static property of type
/// <c>Dapplo.Ini.Ui.Metadata.UiPageMetadata</c> that describes the page title, ordering,
/// change mode, and per-property metadata. This avoids runtime reflection and is
/// AOT / linker-friendly.
/// </para>
/// <para>
/// Example generated output for <c>INetworkSettings</c>:
/// </para>
/// <code>
/// public static partial class NetworkSettingsUiDescriptor
/// {
///     public static Dapplo.Ini.Ui.Metadata.UiPageMetadata Page { get; } = …;
/// }
/// </code>
/// </remarks>
[Generator]
public sealed class UiSectionGenerator : IIncrementalGenerator
{
    // FQNs for IIniSection detection (same as the existing INI generator)
    private const string IIniSectionFqn = "Dapplo.Ini.Interfaces.IIniSection";

    // FQNs for the UI attributes
    private const string UiPageAttributeFqn               = "Dapplo.Ini.Ui.Attributes.UiPageAttribute";
    private const string UiControlAttributeFqn            = "Dapplo.Ini.Ui.Attributes.UiControlAttribute";
    private const string UiGroupAttributeFqn              = "Dapplo.Ini.Ui.Attributes.UiGroupAttribute";
    private const string UiOrderAttributeFqn              = "Dapplo.Ini.Ui.Attributes.UiOrderAttribute";
    private const string UiChangeModeAttributeFqn         = "Dapplo.Ini.Ui.Attributes.UiChangeModeAttribute";
    private const string UiLabelKeyAttributeFqn           = "Dapplo.Ini.Ui.Attributes.UiLabelKeyAttribute";
    private const string UiConditionalVisibilityAttrFqn   = "Dapplo.Ini.Ui.Attributes.UiConditionalVisibilityAttribute";
    private const string UiConditionalEnableAttrFqn       = "Dapplo.Ini.Ui.Attributes.UiConditionalEnableAttribute";

    // FQNs used for ignoring properties
    private const string IgnoreDataMemberAttributeFqn     = "System.Runtime.Serialization.IgnoreDataMemberAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var interfaces = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InterfaceDeclarationSyntax ids
                                               && (ids.AttributeLists.Count > 0
                                                   || ids.BaseList != null),
                transform: static (ctx, _) => GetModel(ctx))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutput(interfaces, static (spc, model) =>
            spc.AddSource($"{model.DescriptorClassName}.g.cs",
                SourceText.From(Emit(model), Encoding.UTF8)));
    }

    // ── Model ─────────────────────────────────────────────────────────────────

    private sealed class PropertyUiModel
    {
        public string Name            { get; set; } = "";
        public string TypeFullName    { get; set; } = "";
        public bool   IsValueType     { get; set; }
        // UiControl
        public string? ControlTypeLiteral { get; set; }          // e.g. "Dapplo.Ini.Ui.Enums.UiControlType.Slider"
        public string? CtrlMinimum    { get; set; }
        public string? CtrlMaximum    { get; set; }
        public string? CtrlIncrement  { get; set; }
        public string? CtrlDecimalPlaces { get; set; }
        public string? CtrlItemsSourceProperty { get; set; }
        public string? CtrlUnit       { get; set; }
        public string? CtrlPlaceholder { get; set; }
        public bool   CtrlHideLabel   { get; set; }
        // UiGroup
        public string? GroupName      { get; set; }
        public int    GroupOrder      { get; set; }
        public string? GroupVisibilityConditionProperty { get; set; }
        public bool   GroupInvertVisibility { get; set; }
        public string? GroupEnableConditionProperty { get; set; }
        public bool   GroupInvertEnable { get; set; }
        // UiOrder
        public int    Order           { get; set; }
        // UiChangeMode (null = inherit from section)
        public string? ChangeModeOverride { get; set; }
        // UiLabelKey
        public string? LabelKey       { get; set; }
        public string? DescriptionKey { get; set; }
        // UiConditionalVisibility
        public string? VisibilityConditionProperty { get; set; }
        public bool   InvertVisibility { get; set; }
        // UiConditionalEnable
        public string? EnableConditionProperty { get; set; }
        public bool   InvertEnable    { get; set; }
    }

    private sealed class SectionUiModel
    {
        public string Namespace            { get; set; } = "";
        public string InterfaceName        { get; set; } = "";
        public string DescriptorClassName  { get; set; } = "";
        // UiPage
        public string? PageTitle           { get; set; }
        public string? PageCategory        { get; set; }
        public int    PageOrder            { get; set; }
        public string? PageIcon            { get; set; }
        // UiChangeMode (section default)
        public string? SectionChangeMode   { get; set; }
        // UiLabelKey on the interface itself
        public string? InterfaceLabelKey   { get; set; }
        public List<PropertyUiModel> Properties { get; set; } = new();
    }

    // ── Extraction ────────────────────────────────────────────────────────────

    private static SectionUiModel? GetModel(GeneratorSyntaxContext ctx)
    {
        var ids = (InterfaceDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(ids) as INamedTypeSymbol;
        if (symbol is null) return null;

        // Only process interfaces that implement IIniSection (excluding IIniSection itself).
        bool implementsIIniSection = symbol.ToDisplayString() != IIniSectionFqn
            && symbol.AllInterfaces.Any(i => i.ToDisplayString() == IIniSectionFqn);
        if (!implementsIIniSection) return null;

        // Only generate a descriptor when at least one UI attribute is present on the
        // interface or one of its properties — avoids bloating projects that don't use
        // the UI framework at all.
        bool hasUiAttributes = HasUiAttribute(symbol);
        if (!hasUiAttributes)
        {
            foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (HasUiAttribute(member))
                {
                    hasUiAttributes = true;
                    break;
                }
            }
        }
        if (!hasUiAttributes) return null;

        var interfaceName = symbol.Name;
        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();

        // ── Section-level attributes ──────────────────────────────────────────
        string? pageTitle = null, pageCategory = null, pageIcon = null;
        int pageOrder = 0;
        string? sectionChangeMode = null;
        string? interfaceLabelKey = null;

        var pageAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UiPageAttributeFqn);
        if (pageAttr != null)
        {
            foreach (var na in pageAttr.NamedArguments)
            {
                switch (na.Key)
                {
                    case "Title"    : pageTitle    = na.Value.Value as string; break;
                    case "Category" : pageCategory = na.Value.Value as string; break;
                    case "Order"    : pageOrder    = na.Value.Value is int o ? o : 0; break;
                    case "Icon"     : pageIcon     = na.Value.Value as string; break;
                }
            }
        }

        // Derive default title from the interface name (strip leading 'I').
        if (pageTitle == null)
            pageTitle = interfaceName.Length > 1 && interfaceName[0] == 'I'
                ? interfaceName.Substring(1) : interfaceName;

        var changeModeAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UiChangeModeAttributeFqn);
        if (changeModeAttr?.ConstructorArguments.Length > 0 &&
            changeModeAttr.ConstructorArguments[0].Value is int cmVal)
        {
            sectionChangeMode = ChangeModeIntToLiteral(cmVal);
        }

        var labelKeyAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UiLabelKeyAttributeFqn);
        if (labelKeyAttr?.ConstructorArguments.Length > 0 &&
            labelKeyAttr.ConstructorArguments[0].Value is string lk)
        {
            interfaceLabelKey = lk;
            if (pageTitle == null) pageTitle = lk; // Use label key as title fallback
        }

        // ── Properties ────────────────────────────────────────────────────────
        var properties = new List<PropertyUiModel>();
        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            // Skip ignored properties
            if (member.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == IgnoreDataMemberAttributeFqn))
                continue;

            var pm = new PropertyUiModel
            {
                Name         = member.Name,
                TypeFullName = member.Type.ToDisplayString(),
                IsValueType  = member.Type.IsValueType,
            };

            // [UiControl]
            var ctrlAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UiControlAttributeFqn);
            if (ctrlAttr != null)
            {
                if (ctrlAttr.ConstructorArguments.Length > 0 &&
                    ctrlAttr.ConstructorArguments[0].Value is int ctrlTypeInt)
                    pm.ControlTypeLiteral = ControlTypeIntToLiteral(ctrlTypeInt);
                foreach (var na in ctrlAttr.NamedArguments)
                {
                    switch (na.Key)
                    {
                        case "Minimum"            : pm.CtrlMinimum = FormatDouble(na.Value.Value); break;
                        case "Maximum"            : pm.CtrlMaximum = FormatDouble(na.Value.Value); break;
                        case "Increment"          : pm.CtrlIncrement = FormatDouble(na.Value.Value); break;
                        case "DecimalPlaces"      : pm.CtrlDecimalPlaces = na.Value.Value?.ToString(); break;
                        case "ItemsSourceProperty": pm.CtrlItemsSourceProperty = na.Value.Value as string; break;
                        case "Unit"               : pm.CtrlUnit = na.Value.Value as string; break;
                        case "Placeholder"        : pm.CtrlPlaceholder = na.Value.Value as string; break;
                        case "HideLabel"          : pm.CtrlHideLabel = na.Value.Value is true; break;
                    }
                }
            }

            // Infer control type when no explicit [UiControl] was given.
            if (pm.ControlTypeLiteral == null)
                pm.ControlTypeLiteral = InferControlType(member.Type);

            // [UiGroup]
            var groupAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UiGroupAttributeFqn);
            if (groupAttr != null)
            {
                if (groupAttr.ConstructorArguments.Length > 0 &&
                    groupAttr.ConstructorArguments[0].Value is string gn)
                    pm.GroupName = gn;
                foreach (var na in groupAttr.NamedArguments)
                {
                    switch (na.Key)
                    {
                        case "Order"                         : pm.GroupOrder = na.Value.Value is int go ? go : 0; break;
                        case "VisibilityConditionProperty"   : pm.GroupVisibilityConditionProperty = na.Value.Value as string; break;
                        case "InvertVisibilityCondition"     : pm.GroupInvertVisibility = na.Value.Value is true; break;
                        case "EnableConditionProperty"       : pm.GroupEnableConditionProperty = na.Value.Value as string; break;
                        case "InvertEnableCondition"         : pm.GroupInvertEnable = na.Value.Value is true; break;
                    }
                }
            }

            // [UiOrder]
            var orderAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UiOrderAttributeFqn);
            if (orderAttr?.ConstructorArguments.Length > 0 && orderAttr.ConstructorArguments[0].Value is int ord)
                pm.Order = ord;
            else
                pm.Order = pm.GroupOrder; // fall back to group order

            // [UiChangeMode]
            var propChangeModeAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UiChangeModeAttributeFqn);
            if (propChangeModeAttr?.ConstructorArguments.Length > 0 &&
                propChangeModeAttr.ConstructorArguments[0].Value is int pcmVal)
                pm.ChangeModeOverride = ChangeModeIntToLiteral(pcmVal);

            // [UiLabelKey]
            var propLabelKeyAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UiLabelKeyAttributeFqn);
            if (propLabelKeyAttr?.ConstructorArguments.Length > 0 &&
                propLabelKeyAttr.ConstructorArguments[0].Value is string plk)
            {
                pm.LabelKey = plk;
                foreach (var na in propLabelKeyAttr.NamedArguments)
                    if (na.Key == "DescriptionKey" && na.Value.Value is string dk)
                        pm.DescriptionKey = dk;
            }

            // [UiConditionalVisibility]
            var visAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UiConditionalVisibilityAttrFqn);
            if (visAttr?.ConstructorArguments.Length > 0 &&
                visAttr.ConstructorArguments[0].Value is string vcp)
            {
                pm.VisibilityConditionProperty = vcp;
                foreach (var na in visAttr.NamedArguments)
                    if (na.Key == "Invert" && na.Value.Value is true)
                        pm.InvertVisibility = true;
            }

            // [UiConditionalEnable]
            var enAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UiConditionalEnableAttrFqn);
            if (enAttr?.ConstructorArguments.Length > 0 &&
                enAttr.ConstructorArguments[0].Value is string ecp)
            {
                pm.EnableConditionProperty = ecp;
                foreach (var na in enAttr.NamedArguments)
                    if (na.Key == "Invert" && na.Value.Value is true)
                        pm.InvertEnable = true;
            }

            properties.Add(pm);
        }

        // Stable sort by Order (preserves declaration order for ties)
        properties.Sort((a, b) => a.Order.CompareTo(b.Order));

        var strippedName = interfaceName.Length > 1 && interfaceName[0] == 'I'
            ? interfaceName.Substring(1) : interfaceName;

        return new SectionUiModel
        {
            Namespace           = ns,
            InterfaceName       = interfaceName,
            DescriptorClassName = $"{strippedName}UiDescriptor",
            PageTitle           = pageTitle,
            PageCategory        = pageCategory,
            PageOrder           = pageOrder,
            PageIcon            = pageIcon,
            SectionChangeMode   = sectionChangeMode,
            InterfaceLabelKey   = interfaceLabelKey,
            Properties          = properties,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool HasUiAttribute(ISymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var fqn = attr.AttributeClass?.ToDisplayString();
            if (fqn != null && fqn.StartsWith("Dapplo.Ini.Ui.Attributes."))
                return true;
        }
        return false;
    }

    private static string ControlTypeIntToLiteral(int value)
    {
        // Enum values match UiControlType declaration order.
        return value switch
        {
            0 => "Dapplo.Ini.Ui.Enums.UiControlType.TextBox",
            1 => "Dapplo.Ini.Ui.Enums.UiControlType.CheckBox",
            2 => "Dapplo.Ini.Ui.Enums.UiControlType.Slider",
            3 => "Dapplo.Ini.Ui.Enums.UiControlType.DropDown",
            4 => "Dapplo.Ini.Ui.Enums.UiControlType.UpDown",
            5 => "Dapplo.Ini.Ui.Enums.UiControlType.RadioButton",
            6 => "Dapplo.Ini.Ui.Enums.UiControlType.MultilineTextBox",
            7 => "Dapplo.Ini.Ui.Enums.UiControlType.ColorPicker",
            8 => "Dapplo.Ini.Ui.Enums.UiControlType.FilePicker",
            9 => "Dapplo.Ini.Ui.Enums.UiControlType.FolderPicker",
            _ => "Dapplo.Ini.Ui.Enums.UiControlType.TextBox",
        };
    }

    private static string InferControlType(ITypeSymbol type)
    {
        // Unwrap Nullable<T>
        var underlying = type;
        if (type is INamedTypeSymbol named && named.IsGenericType
            && named.OriginalDefinition.ToDisplayString() == "System.Nullable<T>")
            underlying = named.TypeArguments[0];

        if (underlying.SpecialType == SpecialType.System_Boolean)
            return "Dapplo.Ini.Ui.Enums.UiControlType.CheckBox";
        if (underlying.TypeKind == TypeKind.Enum)
            return "Dapplo.Ini.Ui.Enums.UiControlType.DropDown";
        if (underlying.SpecialType == SpecialType.System_String)
            return "Dapplo.Ini.Ui.Enums.UiControlType.TextBox";
        if (IsNumericSpecialType(underlying.SpecialType))
            return "Dapplo.Ini.Ui.Enums.UiControlType.UpDown";
        return "Dapplo.Ini.Ui.Enums.UiControlType.TextBox";
    }

    private static bool IsNumericSpecialType(SpecialType st)
    {
        return st == SpecialType.System_Int32 || st == SpecialType.System_Int64
            || st == SpecialType.System_Int16 || st == SpecialType.System_Byte
            || st == SpecialType.System_UInt32 || st == SpecialType.System_UInt64
            || st == SpecialType.System_UInt16 || st == SpecialType.System_SByte
            || st == SpecialType.System_Single || st == SpecialType.System_Double
            || st == SpecialType.System_Decimal;
    }

    private static string ChangeModeIntToLiteral(int value) => value switch
    {
        0 => "Dapplo.Ini.Ui.Enums.UiChangeMode.Immediate",
        1 => "Dapplo.Ini.Ui.Enums.UiChangeMode.OnConfirm",
        _ => "Dapplo.Ini.Ui.Enums.UiChangeMode.Immediate",
    };

    private static string? FormatDouble(object? value)
    {
        if (value is double d)
        {
            if (double.IsPositiveInfinity(d) || d >= double.MaxValue) return "double.MaxValue";
            if (double.IsNegativeInfinity(d) || d <= double.MinValue) return "double.MinValue";
            return d.ToString("R") + "d";
        }
        return null;
    }

    private static string EscapeString(string? s) =>
        s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ── Code emission ─────────────────────────────────────────────────────────

    private static string Emit(SectionUiModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        bool hasNamespace = !string.IsNullOrEmpty(m.Namespace);
        if (hasNamespace)
        {
            sb.AppendLine($"namespace {m.Namespace}");
            sb.AppendLine("{");
        }

        string indent = hasNamespace ? "    " : "";
        string i2 = indent + "    ";
        string i3 = i2 + "    ";
        string i4 = i3 + "    ";

        sb.AppendLine($"{indent}/// <summary>");
        sb.AppendLine($"{indent}/// Compile-time UI descriptor for <see cref=\"{m.InterfaceName}\"/>.");
        sb.AppendLine($"{indent}/// Provides a <see cref=\"Dapplo.Ini.Ui.Metadata.UiPageMetadata\"/> without runtime reflection.");
        sb.AppendLine($"{indent}/// </summary>");
        sb.AppendLine($"{indent}public static partial class {m.DescriptorClassName}");
        sb.AppendLine($"{indent}{{");

        sb.AppendLine($"{i2}/// <summary>The UI page metadata for <see cref=\"{m.InterfaceName}\"/>.</summary>");
        sb.AppendLine($"{i2}public static global::Dapplo.Ini.Ui.Metadata.UiPageMetadata Page {{ get; }} =");
        sb.AppendLine($"{i3}new global::Dapplo.Ini.Ui.Metadata.UiPageMetadata");
        sb.AppendLine($"{i3}{{");

        sb.AppendLine($"{i4}SectionType = typeof({m.InterfaceName}),");
        sb.AppendLine($"{i4}Title = \"{EscapeString(m.PageTitle)}\",");
        if (m.PageCategory != null)
            sb.AppendLine($"{i4}Category = \"{EscapeString(m.PageCategory)}\",");
        sb.AppendLine($"{i4}Order = {m.PageOrder},");
        if (m.PageIcon != null)
            sb.AppendLine($"{i4}Icon = \"{EscapeString(m.PageIcon)}\",");
        if (m.SectionChangeMode != null)
            sb.AppendLine($"{i4}DefaultChangeMode = {m.SectionChangeMode},");

        // Properties array
        sb.AppendLine($"{i4}Properties = new global::System.Collections.ObjectModel.ReadOnlyCollection<global::Dapplo.Ini.Ui.Metadata.UiPropertyMetadata>(");
        sb.AppendLine($"{i4}    new global::Dapplo.Ini.Ui.Metadata.UiPropertyMetadata[]");
        sb.AppendLine($"{i4}    {{");

        string effectiveChangeMode = m.SectionChangeMode ?? "Dapplo.Ini.Ui.Enums.UiChangeMode.Immediate";

        foreach (var p in m.Properties)
        {
            sb.AppendLine($"{i4}        new global::Dapplo.Ini.Ui.Metadata.UiPropertyMetadata");
            sb.AppendLine($"{i4}        {{");
            sb.AppendLine($"{i4}            PropertyName = \"{EscapeString(p.Name)}\",");
            sb.AppendLine($"{i4}            ControlType = global::{p.ControlTypeLiteral},");
            if (p.GroupName != null)
                sb.AppendLine($"{i4}            GroupName = \"{EscapeString(p.GroupName)}\",");
            sb.AppendLine($"{i4}            Order = {p.Order},");
            string propChangeMode = p.ChangeModeOverride ?? effectiveChangeMode;
            sb.AppendLine($"{i4}            ChangeMode = global::{propChangeMode},");
            if (p.LabelKey != null)
                sb.AppendLine($"{i4}            LabelKey = \"{EscapeString(p.LabelKey)}\",");
            if (p.DescriptionKey != null)
                sb.AppendLine($"{i4}            DescriptionKey = \"{EscapeString(p.DescriptionKey)}\",");
            if (p.VisibilityConditionProperty != null)
            {
                sb.AppendLine($"{i4}            VisibilityConditionProperty = \"{EscapeString(p.VisibilityConditionProperty)}\",");
                if (p.InvertVisibility)
                    sb.AppendLine($"{i4}            InvertVisibility = true,");
            }
            if (p.EnableConditionProperty != null)
            {
                sb.AppendLine($"{i4}            EnableConditionProperty = \"{EscapeString(p.EnableConditionProperty)}\",");
                if (p.InvertEnable)
                    sb.AppendLine($"{i4}            InvertEnable = true,");
            }
            sb.AppendLine($"{i4}        }},");
        }

        sb.AppendLine($"{i4}    }})");
        sb.AppendLine($"{i3}}};");
        sb.AppendLine($"{indent}}}");

        if (hasNamespace)
            sb.AppendLine("}");

        return sb.ToString();
    }
}
