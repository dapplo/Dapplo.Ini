// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using BenchmarkDotNet.Attributes;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Configuration;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Benchmarks;

[IniSection("Application")]
public interface IBenchApplicationSettings : IIniSection, INotifyPropertyChanged
{
    [DefaultValue("MyProduct")] string? ProductName { get; set; }
    [DefaultValue(8080)] int Port { get; set; }
    [DefaultValue(true)] bool EnableLogging { get; set; }
    [DefaultValue(3.5)] double Threshold { get; set; }
    [DefaultValue("00:00:30")] TimeSpan Timeout { get; set; }
    [DefaultValue("first,second,third")] List<string>? RecentFiles { get; set; }
    [IniValue(DefaultValue = "1;2;3;4;5", ListDelimiter = ';')] List<int>? Numbers { get; set; }
    [DefaultValue("a=1,b=2,c=3")] Dictionary<string, int>? Weights { get; set; }
    DayOfWeek FirstDay { get; set; }
    int? OptionalLimit { get; set; }
}

[IniSection("Ui")]
public interface IBenchUiSettings : IIniSection
{
    [DefaultValue("Dark")] string? Theme { get; set; }
    [DefaultValue(1024)] int Width { get; set; }
    [DefaultValue(768)] int Height { get; set; }
    [DefaultValue("10,20")] int[]? Position { get; set; }
    [DefaultValue("en-US")] string? Language { get; set; }
    [DefaultValue(false)] bool Maximized { get; set; }
}

/// <summary>A configuration as an application uses it: load (reload), save and the async variants.</summary>
[MemoryDiagnoser]
public class ConfigBenchmarks
{
    private IniConfig _config = null!;
    private string _fileName = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MyProductBenchmarks");
        Directory.CreateDirectory(directory);
        _fileName = $"config{Environment.ProcessId}.ini";
        File.WriteAllText(Path.Combine(directory, _fileName),
            "[Application]\nProductName = MyProduct Pro\nPort = 9090\nEnableLogging = False\nThreshold = 1.25\n" +
            "Timeout = 00:01:00\nRecentFiles = c:\\one.txt,c:\\two.txt,c:\\three.txt,c:\\four.txt\nNumbers = 9;8;7;6;5;4\n" +
            "Weights = x=10,y=20,z=30\nFirstDay = Monday\nOptionalLimit = 12\n\n" +
            "[Ui]\nTheme = Light\nWidth = 1920\nHeight = 1080\nPosition = 100,200\nLanguage = de-DE\nMaximized = True\n");
        _config = IniConfigRegistry.ForFile(_fileName)
            .AddSearchPath(directory)
            .RegisterSection<IBenchApplicationSettings>(new BenchApplicationSettingsImpl())
            .RegisterSection<IBenchUiSettings>(new BenchUiSettingsImpl())
            .Build();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _config.Dispose();
        IniConfigRegistry.Unregister(_fileName);
    }

    [Benchmark]
    public void Reload() => _config.Reload();

    [Benchmark]
    public Task ReloadAsync() => _config.ReloadAsync();

    [Benchmark]
    public void Save() => _config.Save();

    [Benchmark]
    public Task SaveAsync() => _config.SaveAsync();
}
