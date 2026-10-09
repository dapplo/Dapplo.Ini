// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Dapplo.Ini.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Dapplo.Ini.Generator.Tests;

/// <summary>
/// Runs the source generators on small source snippets and checks their diagnostics, their output
/// and that unchanged interfaces are served from the incremental cache.
/// </summary>
public sealed class GeneratorDiagnosticsTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Dapplo.Ini.IniConfig).Assembly.Location));
        return CSharpCompilation.Create(
            "GeneratorTest",
            sources.Select(s => CSharpSyntaxTree.ParseText(s, ParseOptions)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    /// <summary>
    /// The warnings the compiler reports for the generated code (what TreatWarningsAsErrors would turn into errors)
    /// </summary>
    private static (ImmutableArray<Diagnostic> GeneratedCodeWarnings, ImmutableArray<Diagnostic> CompileErrors, GeneratorDriverRunResult Result)
        RunAndCollectGeneratedWarnings(params string[] sources)
    {
        var compilation = CreateCompilation(sources);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new IniSectionGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult();
        var generatedTrees = new HashSet<SyntaxTree>(result.GeneratedTrees);
        var diagnostics = output.GetDiagnostics();
        var warnings = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning && d.Location.SourceTree != null && generatedTrees.Contains(d.Location.SourceTree))
            .ToImmutableArray();
        return (warnings, diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray(), result);
    }

    private static (ImmutableArray<Diagnostic> GeneratorDiagnostics, ImmutableArray<Diagnostic> CompileErrors, GeneratorDriverRunResult Result)
        Run(params string[] sources)
    {
        var compilation = CreateCompilation(sources);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new IniSectionGenerator().AsSourceGenerator(), new IniLanguageSectionGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        return (generatorDiagnostics, errors, driver.GetRunResult());
    }

    [Fact]
    public void ValidSection_GeneratesWithoutDiagnosticsOrErrors()
    {
        var (diagnostics, errors, result) = Run("""
            using Dapplo.Ini.Interfaces;
            namespace Sample;
            public interface ISettings : IIniSection
            {
                string? Name { get; set; }
                int Count { get; set; }
                System.Collections.Generic.List<System.TimeSpan>? Delays { get; set; }
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.EndsWith("Sample.SettingsImpl.g.cs"));
    }

    [Fact]
    public void Dictionaries_RuntimeOnlyAndIgnored_GenerateNoUnusedFieldWarnings()
    {
        var (warnings, errors, result) = RunAndCollectGeneratedWarnings("""
            using System.Collections.Generic;
            using System.Runtime.Serialization;
            using Dapplo.Ini.Attributes;
            using Dapplo.Ini.Interfaces;
            namespace Sample;
            public interface ISettings : IIniSection
            {
                Dictionary<string, int>? Persisted { get; set; }
                [IniValue(RuntimeOnly = true)] Dictionary<string, string>? RuntimeHistory { get; set; }
                [IgnoreDataMember] Dictionary<string, string>? Ignored { get; set; }
            }
            """);

        Assert.Empty(errors);
        Assert.Empty(warnings);
        var generated = Assert.Single(result.GeneratedTrees, t => t.FilePath.EndsWith("Sample.SettingsImpl.g.cs")).ToString();
        // The persisted dictionary still tracks whether the file supplied entries
        Assert.Contains("_persistedHasRawEntries", generated);
        Assert.DoesNotContain("_runtimeHistoryHasRawEntries", generated);
        Assert.DoesNotContain("_ignoredHasRawEntries", generated);
    }

    [Fact]
    public void DuplicateIniKey_IsReportedAsDINI001()
    {
        var (diagnostics, _, _) = Run("""
            using Dapplo.Ini.Attributes;
            using Dapplo.Ini.Interfaces;
            namespace Sample;
            public interface ISettings : IIniSection
            {
                string? Name { get; set; }
                [IniValue(KeyName = "NAME")] string? Other { get; set; }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "DINI001");
        Assert.Contains("'Name' and 'Other'", diagnostic.GetMessage());
    }

    [Fact]
    public void TypeWithoutBuiltInConverter_IsReportedAsDINI002()
    {
        var (diagnostics, errors, _) = Run("""
            using Dapplo.Ini.Interfaces;
            namespace Sample;
            public sealed class Point { public int X; }
            public interface ISettings : IIniSection
            {
                Point? Location { get; set; }
                int? Known { get; set; }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "DINI002");
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("Location", diagnostic.GetMessage());
        Assert.Empty(errors);
    }

    [Fact]
    public void GenericSectionInterface_IsReportedAsDINI003_AndSkipped()
    {
        var (diagnostics, _, result) = Run("""
            using Dapplo.Ini.Interfaces;
            namespace Sample;
            public interface ISettings<T> : IIniSection
            {
                T? Value { get; set; }
            }
            """);

        Assert.Single(diagnostics, d => d.Id == "DINI003");
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("SettingsImpl"));
    }

    [Fact]
    public void TwoInterfacesGeneratingTheSameClass_AreReportedAsDINI005()
    {
        var (diagnostics, errors, _) = Run("""
            using Dapplo.Ini.Interfaces;
            namespace Sample;
            public static class A { public interface ISettings : IIniSection { string? X { get; set; } } }
            public static class B { public interface ISettings : IIniSection { string? Y { get; set; } } }
            """);

        Assert.Single(diagnostics, d => d.Id == "DINI005");
        Assert.DoesNotContain(errors, e => e.Id == "CS0101"); // no duplicate type definition
    }

    [Fact]
    public void PartialInterface_IsGeneratedOnce()
    {
        var (diagnostics, errors, result) = Run("""
            using Dapplo.Ini.Interfaces;
            namespace Sample;
            public partial interface ISettings : IIniSection { string? A { get; set; } }
            [System.ComponentModel.Description("more")] public partial interface ISettings { }
            """);

        Assert.Empty(diagnostics);
        Assert.Single(result.GeneratedTrees, t => t.FilePath.EndsWith("Sample.SettingsImpl.g.cs"));
        Assert.DoesNotContain(errors, e => e.Id == "CS0101");
    }

    [Fact]
    public void LanguagePropertyWithSetter_IsReportedAsDINI101()
    {
        var (diagnostics, _, _) = Run("""
            using Dapplo.Ini.Internationalization.Attributes;
            namespace Sample;
            [IniLanguageSection]
            public interface IMainLanguage
            {
                string Title { get; }
                string Broken { get; set; }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "DINI101");
        Assert.Contains("Broken", diagnostic.GetMessage());
    }

    [Fact]
    public void LanguageSection_NestedAndNameWithLineBreak_Compiles()
    {
        var (diagnostics, errors, _) = Run("""
            using Dapplo.Ini.Internationalization.Attributes;
            namespace Sample;
            public static class Outer
            {
                [IniLanguageSection("Line\nBreak")]
                public interface IMainLanguage { string Title { get; } }
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
    }

    [Fact]
    public void UnrelatedEdit_ServesSectionsFromTheCache()
    {
        var compilation = CreateCompilation("""
            using Dapplo.Ini.Interfaces;
            namespace Sample;
            public interface ISettings : IIniSection { string? Name { get; set; } }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new IniSectionGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        var edited = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Sample; public class Unrelated { }", ParseOptions));
        driver = driver.RunGenerators(edited);

        var steps = driver.GetRunResult().Results[0].TrackedSteps["Dapplo.Ini.Sections"];
        Assert.All(steps.SelectMany(s => s.Outputs), output =>
            Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, output.Reason.ToString()));
    }
}
