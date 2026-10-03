// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Dapplo.Ini.Generator;
using Dapplo.Ini.Internationalization.Configuration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Dapplo.Ini.Generator.Tests;

/// <summary>
/// Greenshot's main language interface has several hundred texts: generating, compiling and using a
/// language section with 600 properties must not be noticeably slow.
/// </summary>
public sealed class LargeLanguageSectionTests
{
    private const int PropertyCount = 600;
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static string CreateSource()
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Dapplo.Ini.Internationalization.Attributes;");
        sb.AppendLine("namespace Sample;");
        sb.AppendLine("[IniLanguageSection(\"Core\")]");
        sb.AppendLine("public interface IHugeLanguage : System.ComponentModel.INotifyPropertyChanged, System.Collections.Generic.IReadOnlyDictionary<string, string>");
        sb.AppendLine("{");
        for (var i = 0; i < PropertyCount; i++)
            sb.AppendLine($"    string Text{i} {{ get; }}");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Dapplo.Ini.IniConfig).Assembly.Location));
        return CSharpCompilation.Create(
            "HugeLanguageTest",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
                optimizationLevel: OptimizationLevel.Release));
    }

    [Fact]
    public void LanguageSection_With600Properties_GeneratesCompilesAndNotifiesQuickly()
    {
        var compilation = CreateCompilation(CreateSource());
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new IniLanguageSectionGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);

        // Generation
        var stopwatch = Stopwatch.StartNew();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var generationTime = stopwatch.Elapsed;

        Assert.Empty(generatorDiagnostics);
        // UpdateTranslations is table-driven: no locals per property (with them the JIT of the first language
        // load took ~100 ms for 600 properties), so its size does not grow with the number of properties.
        var generated = driver.GetRunResult().GeneratedTrees.Single().ToString();
        Assert.DoesNotContain("__new_", generated);
        Assert.DoesNotContain("__changed_", generated);
        var update = generated.Substring(generated.IndexOf("public override void UpdateTranslations", StringComparison.Ordinal));
        Assert.True(update.Length < 5000, $"UpdateTranslations has {update.Length} characters");
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        Assert.Empty(errors);
        // Generous bound for slow CI machines; locally this takes a few hundred milliseconds including Roslyn warm-up.
        Assert.True(generationTime < TimeSpan.FromSeconds(10), $"Generation took {generationTime}");

        // A second run without changes is served from the incremental cache.
        stopwatch.Restart();
        driver = driver.RunGenerators(compilation);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Cached generation took {stopwatch.Elapsed}");

        // Compile and load the generated class
        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("HugeLanguage", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            var section = (LanguageSectionBase)Activator.CreateInstance(assembly.GetType("Sample.HugeLanguageImpl")!)!;
            var changed = new List<string?>();
            ((INotifyPropertyChanged)section).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            var english = Enumerable.Range(0, PropertyCount).ToDictionary(i => $"text{i}", i => $"Text {i}");
            stopwatch.Restart();
            section.UpdateTranslations(english);
            var firstUpdate = stopwatch.Elapsed;

            Assert.Equal(PropertyCount + 1, changed.Count);   // every property + "Item[]"
            Assert.Equal("Text 599", assembly.GetType("Sample.HugeLanguageImpl")!.GetProperty("Text599")!.GetValue(section));

            // A language switch that changes ten texts raises PropertyChanged for exactly those ten (+ Item[]).
            changed.Clear();
            var german = new Dictionary<string, string>(english);
            for (var i = 0; i < 10; i++)
                german[$"text{i * 60}"] = $"Text {i * 60} (de)";
            stopwatch.Restart();
            section.UpdateTranslations(german);
            var secondUpdate = stopwatch.Elapsed;

            Assert.Equal(Enumerable.Range(0, 10).Select(i => $"Text{i * 60}").Append("Item[]"), changed);
            // Generous bounds for slow CI machines (locally ~3 ms and < 1 ms); the structure is checked below.
            Assert.True(firstUpdate < TimeSpan.FromSeconds(2), $"First update (incl. JIT) took {firstUpdate}");
            Assert.True(secondUpdate < TimeSpan.FromMilliseconds(500), $"Second update took {secondUpdate}");
        }
        finally
        {
            context.Unload();
        }
    }
}
