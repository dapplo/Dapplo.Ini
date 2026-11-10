// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using BenchmarkDotNet.Attributes;
using Dapplo.Ini.Parsing;

namespace Dapplo.Ini.Benchmarks;

/// <summary>Parsing and writing of a small (typical application) and a large (~1 MB) file.</summary>
[MemoryDiagnoser]
public class ParserBenchmarks
{
    private string _content = string.Empty;
    private string _filePath = string.Empty;
    private string _writePath = string.Empty;
    private IniFile _parsed = null!;

    [Params(10, 400)]
    public int Sections { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _content = SampleContent.Create(Sections, 40);
        var directory = Path.Combine(Path.GetTempPath(), "MyProductBenchmarks");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, $"parse{Sections}.ini");
        _writePath = Path.Combine(directory, $"write{Sections}.ini");
        File.WriteAllText(_filePath, _content, Encoding.UTF8);
        _parsed = IniFileParser.Parse(_content);
    }

    [Benchmark]
    public IniFile ParseString() => IniFileParser.Parse(_content);

    [Benchmark]
    public IniFile ParseFile() => IniFileParser.ParseFile(_filePath);

    [Benchmark]
    public Task<IniFile> ParseFileAsync() => IniFileParser.ParseFileAsync(_filePath);

    [Benchmark]
    public string WriteToString() => IniFileWriter.WriteToString(_parsed);

    [Benchmark]
    public void WriteFile() => IniFileWriter.WriteFile(_writePath, _parsed);

    [Benchmark]
    public Task WriteFileAsync() => IniFileWriter.WriteFileAsync(_writePath, _parsed);
}
