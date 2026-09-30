// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Dapplo.Ini.Generator;

/// <summary>Diagnostics reported by the Dapplo.Ini source generators.</summary>
internal static class Diagnostics
{
    private const string Category = "Dapplo.Ini";

    public static readonly DiagnosticDescriptor DuplicateKey = new(
        "DINI001", "Duplicate INI key",
        "Properties '{0}' and '{1}' of '{2}' both use the INI key '{3}'; give one of them another key with [IniValue(KeyName = \"...\")] or [DataMember(Name = \"...\")]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoBuiltInConverter = new(
        "DINI002", "No built-in converter for the property type",
        "Property '{0}' of '{1}' has type '{2}', which has no built-in converter; register one with ValueConverterRegistry.Register before the configuration is loaded, otherwise the value is not read from or written to the file",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenericInterface = new(
        "DINI003", "Generic section interfaces are not supported",
        "'{0}' is generic; no implementation is generated for it",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateClassName = new(
        "DINI005", "Generated class name is already used",
        "No implementation is generated for '{0}': '{1}' already generates the class '{2}' in the same namespace; rename one of the interfaces",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor LanguagePropertyShape = new(
        "DINI101", "Language section properties must be get-only strings",
        "Property '{0}' of language section '{1}' must be declared as 'string {0} {{ get; }}'",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}

/// <summary>
/// A diagnostic captured in the (cached) generator pipeline. It holds only values, no syntax trees or
/// symbols, so the incremental pipeline can compare it and cache the result.
/// </summary>
internal sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
{
    private readonly DiagnosticDescriptor _descriptor;
    private readonly string[] _arguments;
    private readonly string? _filePath;
    private readonly TextSpan _span;
    private readonly LinePositionSpan _lineSpan;

    private DiagnosticInfo(DiagnosticDescriptor descriptor, Location? location, string[] arguments)
    {
        _descriptor = descriptor;
        _arguments = arguments;
        if (location != null && location.IsInSource)
        {
            _filePath = location.SourceTree?.FilePath;
            _span = location.SourceSpan;
            _lineSpan = location.GetLineSpan().Span;
        }
    }

    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, ISymbol? symbol, params string[] arguments)
        => new(descriptor, symbol?.Locations.FirstOrDefault(), arguments);

    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location? location, params string[] arguments)
        => new(descriptor, location, arguments);

    public Diagnostic ToDiagnostic()
    {
        var location = _filePath != null ? Location.Create(_filePath, _span, _lineSpan) : Location.None;
        return Diagnostic.Create(_descriptor, location, _arguments.Cast<object>().ToArray());
    }

    public bool Equals(DiagnosticInfo? other)
        => other != null
           && ReferenceEquals(_descriptor, other._descriptor)
           && _arguments.SequenceEqual(other._arguments)
           && _filePath == other._filePath
           && _span.Equals(other._span);

    public override bool Equals(object? obj) => Equals(obj as DiagnosticInfo);

    public override int GetHashCode() => (_descriptor.Id, _filePath, _span).GetHashCode();
}

/// <summary>
/// The outcome of generating one interface: the source text (or <c>null</c>) plus its diagnostics.
/// Only strings and values, so the incremental pipeline caches it and re-emits nothing when an edit
/// elsewhere does not change it.
/// </summary>
internal sealed class GenerationResult : IEquatable<GenerationResult>
{
    private readonly string? _filePath;
    private readonly TextSpan _span;
    private readonly LinePositionSpan _lineSpan;

    public GenerationResult(string interfaceKey, string classKey, string hintName, string? source,
        ISymbol interfaceSymbol, IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        InterfaceKey = interfaceKey;
        ClassKey = classKey;
        HintName = hintName;
        Source = source;
        Diagnostics = diagnostics;
        var location = interfaceSymbol.Locations.FirstOrDefault();
        if (location != null && location.IsInSource)
        {
            _filePath = location.SourceTree?.FilePath;
            _span = location.SourceSpan;
            _lineSpan = location.GetLineSpan().Span;
        }
    }

    /// <summary>Fully qualified interface name: a partial interface produces several results with the same key.</summary>
    public string InterfaceKey { get; }

    /// <summary>Namespace + generated class name: two interfaces must not produce the same class.</summary>
    public string ClassKey { get; }

    public string HintName { get; }
    public string? Source { get; }
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }

    private Location InterfaceLocation
        => _filePath != null ? Location.Create(_filePath, _span, _lineSpan) : Location.None;

    public bool Equals(GenerationResult? other)
        => other != null
           && InterfaceKey == other.InterfaceKey
           && ClassKey == other.ClassKey
           && HintName == other.HintName
           && Source == other.Source
           && _filePath == other._filePath
           && _span.Equals(other._span)
           && Diagnostics.SequenceEqual(other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as GenerationResult);

    public override int GetHashCode() => (InterfaceKey, Source?.Length ?? 0).GetHashCode();

    /// <summary>
    /// Adds the sources and reports the diagnostics of all results: each interface once (partial interfaces
    /// produce one result per declaration), and no two results that would generate the same class.
    /// </summary>
    public static void Output(SourceProductionContext context, IEnumerable<GenerationResult> results)
    {
        var interfaces = new HashSet<string>(StringComparer.Ordinal);
        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var result in results.OrderBy(r => r.InterfaceKey, StringComparer.Ordinal))
        {
            if (!interfaces.Add(result.InterfaceKey))
                continue;
            foreach (var diagnostic in result.Diagnostics)
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            if (result.Source == null)
                continue;
            if (classes.TryGetValue(result.ClassKey, out var other))
            {
                context.ReportDiagnostic(Diagnostic.Create(global::Dapplo.Ini.Generator.Diagnostics.DuplicateClassName,
                    result.InterfaceLocation, result.InterfaceKey, other, result.ClassKey));
                continue;
            }
            classes[result.ClassKey] = result.InterfaceKey;
            context.AddSource(result.HintName, SourceText.From(result.Source, Encoding.UTF8));
        }
    }
}

/// <summary>Helpers shared by the generators.</summary>
internal static class GeneratorText
{
    /// <summary><c>ISettings</c> → the "I" is a prefix; <c>Interval</c> or <c>I</c> → it is not.</summary>
    public static bool HasInterfacePrefix(string interfaceName)
        => interfaceName.Length > 1 && interfaceName[0] == 'I' && char.IsUpper(interfaceName[1]);

    /// <summary>The name without an "I" prefix (see <see cref="HasInterfacePrefix"/>).</summary>
    public static string StripInterfacePrefix(string interfaceName)
        => HasInterfacePrefix(interfaceName) ? interfaceName.Substring(1) : interfaceName;

    /// <summary>Escapes <paramref name="s"/> for use inside a regular C# string literal.</summary>
    public static string EscapeString(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                case '\u0085': sb.Append("\\u0085"); break;
                case '\u2028': sb.Append("\\u2028"); break;
                case '\u2029': sb.Append("\\u2029"); break;
                default:
                    if (char.IsControl(c))
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>The hint name for a generated file: namespace-qualified, so equal class names in different namespaces do not collide.</summary>
    public static string HintName(string ns, string className)
        => string.IsNullOrEmpty(ns) ? $"{className}.g.cs" : $"{ns}.{className}.g.cs";
}
