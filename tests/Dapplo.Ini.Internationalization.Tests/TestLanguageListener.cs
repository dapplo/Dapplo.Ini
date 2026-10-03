// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Dapplo.Ini.Internationalization.Interfaces;

namespace Dapplo.Ini.Internationalization.Tests;

/// <summary>Records every listener callback of a language configuration.</summary>
internal sealed class TestLanguageListener : LanguageConfigListenerBase
{
    public ConcurrentQueue<string> FilesLoaded { get; } = new();
    public ConcurrentQueue<(string Section, bool Loaded)> SectionsAdded { get; } = new();
    public ConcurrentQueue<(string Operation, Exception Exception)> Errors { get; } = new();
    public ConcurrentQueue<(string Requested, string Resolved)> Resolved { get; } = new();
    public ConcurrentQueue<(string? ModuleOrSection, string Key)> NotFound { get; } = new();
    public ConcurrentQueue<(string Section, string Key, Exception Exception)> FormatFailures { get; } = new();

    public override void OnFileLoaded(string filePath) => FilesLoaded.Enqueue(filePath);
    public override void OnSectionAdded(string sectionName, bool loaded) => SectionsAdded.Enqueue((sectionName, loaded));
    public override void OnError(string operation, Exception exception) => Errors.Enqueue((operation, exception));
    public override void OnLanguageResolved(string requested, string resolved) => Resolved.Enqueue((requested, resolved));
    public override void OnTranslationNotFound(string? moduleOrSection, string key) => NotFound.Enqueue((moduleOrSection, key));
    public override void OnFormatFailed(string sectionName, string key, Exception exception) => FormatFailures.Enqueue((sectionName, key, exception));
}

/// <summary>Creates a temporary directory per test and resets the language registry.</summary>
public abstract class TempDirectoryTestBase : IDisposable
{
    protected TempDirectoryTestBase()
    {
        Root = Path.Combine(Path.GetTempPath(), "dapplo-i18n-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        LanguageConfigRegistry.Clear();
    }

    /// <summary>The temporary directory of this test.</summary>
    protected string Root { get; }

    /// <summary>Creates (if needed) and returns a sub directory of <see cref="Root"/>.</summary>
    protected string Dir(string name)
    {
        var dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Writes a UTF-8 file and returns its path.</summary>
    protected static string Write(string directory, string fileName, string content)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content.Replace("\r\n", "\n"), System.Text.Encoding.UTF8);
        return path;
    }

    public virtual void Dispose()
    {
        LanguageConfigRegistry.Clear();
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { /* a watcher may still hold the directory for a moment */ }
        catch (UnauthorizedAccessException) { }
    }
}
