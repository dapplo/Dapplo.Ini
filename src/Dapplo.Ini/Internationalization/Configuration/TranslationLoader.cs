// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using Dapplo.Ini.Parsing;

namespace Dapplo.Ini.Internationalization.Configuration;

/// <summary>
/// Builds the translations of one or more sections from language files with as few heap allocations as possible.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Files are streamed through a pooled buffer (<see cref="PooledLineReader"/>): no string for the whole file
///   (which would land on the large object heap for big language files) and none per line.</item>
///   <item>Each file is read once for all sections that use it; only keys of registered sections are allocated.</item>
///   <item>Files are applied in reverse order (most specific language and highest priority search path first) and a
///   key that is already set by a more specific file is skipped, so fallback texts that would be overridden are never
///   allocated. Within one file the last occurrence of a key still wins.</item>
///   <item>Keys are normalized in a stack buffer. On .NET 9+ key strings of earlier files and of the previous load are
///   reused, and a value equal to the previous load's value reuses that string (a reload of unchanged files
///   allocates no strings at all).</item>
/// </list>
/// </remarks>
internal sealed class TranslationLoader
{
    private const int MaxStackValueLength = 512;

    private readonly List<Target> _fileTargets = new();
    private readonly List<Target> _current = new();
    // (target, key) of the keys set by the file that is being read: a repeated key in the same file overrides.
    private readonly HashSet<(int Target, string Key)> _setByFile = new();

    /// <summary>The translations of one section that are being built.</summary>
    internal sealed class Target
    {
        public Target(int index, string sectionName, Dictionary<string, string>? previous)
        {
            Index = index;
            SectionName = sectionName;
            Previous = previous;
            Result = new Dictionary<string, string>(previous?.Count ?? 0, StringComparer.OrdinalIgnoreCase);
        }

        public int Index { get; }
        public string SectionName { get; }
        public Dictionary<string, string>? Previous { get; }
        public Dictionary<string, string> Result { get; }
    }

    /// <summary>Starts reading a file that feeds <paramref name="targets"/>.</summary>
    public void BeginFile(IEnumerable<Target> targets)
    {
        _fileTargets.Clear();
        _fileTargets.AddRange(targets);
        _current.Clear();
        _setByFile.Clear();
    }

    /// <summary>Releases the references to the targets of the last build (the collections keep their capacity).</summary>
    public void Reset()
    {
        _fileTargets.Clear();
        _current.Clear();
        _setByFile.Clear();
    }

    /// <summary>Reads a whole file synchronously.</summary>
    public void ReadFile(string path, IEnumerable<Target> targets)
    {
        BeginFile(targets);
        using var reader = PooledLineReader.OpenFile(path);
        while (true)
        {
            while (reader.TryReadLine(out var line))
                ProcessLine(line);
            if (reader.IsCompleted) break;
            reader.Fill();
        }
    }

    /// <summary>Reads a whole file asynchronously.</summary>
    public async Task ReadFileAsync(string path, IEnumerable<Target> targets, CancellationToken cancellationToken)
    {
        BeginFile(targets);
        using var reader = PooledLineReader.OpenFile(path, asynchronous: true);
        while (true)
        {
            ProcessBufferedLines(reader);
            if (reader.IsCompleted) break;
            await reader.FillAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // Spans cannot live in async methods; this processes what is buffered.
    private void ProcessBufferedLines(PooledLineReader reader)
    {
        while (reader.TryReadLine(out var line))
            ProcessLine(line);
    }

    /// <summary>
    /// Processes one line: only keys inside a <c>[Section]</c> of a target are read; only whole lines starting with
    /// <c>;</c> or <c>#</c> are comments; the reserved <c>[__language__]</c> section is never routed to a target.
    /// </summary>
    public void ProcessLine(ReadOnlySpan<char> line)
    {
        var trimmed = line.Trim();
        if (trimmed.IsEmpty) return;

        var first = trimmed[0];
        if (first == ';' || first == '#') return;

        if (first == '[')
        {
            var close = trimmed.IndexOf(']');
            if (close > 1)
            {
                _current.Clear();
                var header = trimmed.Slice(1, close - 1).Trim();
                if (header.Equals(LanguageConfig.LanguageSectionName.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    return;
                foreach (var target in _fileTargets)
                    if (header.Equals(target.SectionName.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        _current.Add(target);
            }
            return;
        }

        if (_current.Count == 0) return;

        var eq = trimmed.IndexOf('=');
        if (eq <= 0) return;

        var rawKey = trimmed.Slice(0, eq);
        var rawValue = trimmed.Slice(eq + 1).TrimStart();

        char[]? pooledKey = null;
        char[]? pooledValue = null;
        try
        {
            var keyBuffer = rawKey.Length <= LanguageSectionBase.MaxStackKeyLength
                ? stackalloc char[rawKey.Length]
                : (pooledKey = ArrayPool<char>.Shared.Rent(rawKey.Length));
            var key = keyBuffer.Slice(0, LanguageSectionBase.NormalizeKey(rawKey, keyBuffer));

            var valueBuffer = rawValue.Length <= MaxStackValueLength
                ? stackalloc char[rawValue.Length]
                : (pooledValue = ArrayPool<char>.Shared.Rent(rawValue.Length));
            var value = valueBuffer.Slice(0, LanguageConfig.UnescapeValue(rawValue, valueBuffer));

            foreach (var target in _current)
                Set(target, key, value);
        }
        finally
        {
            if (pooledKey != null) ArrayPool<char>.Shared.Return(pooledKey);
            if (pooledValue != null) ArrayPool<char>.Shared.Return(pooledValue);
        }
    }

    private void Set(Target target, ReadOnlySpan<char> key, ReadOnlySpan<char> value)
    {
        var result = target.Result;
#if NET9_0_OR_GREATER
        if (result.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(key, out var existingKey, out _))
        {
            // Set by a more specific file: keep. Set earlier in this file: the later line wins.
            if (_setByFile.Contains((target.Index, existingKey)))
                result[existingKey] = CreateValue(target, existingKey, value);
            return;
        }

        string keyString;
        if (target.Previous != null
            && target.Previous.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(key, out var previousKey, out _))
            keyString = previousKey;
        else
            keyString = key.ToString();
#else
        var keyString = key.ToString();
        if (result.ContainsKey(keyString))
        {
            if (_setByFile.Contains((target.Index, keyString)))
                result[keyString] = CreateValue(target, keyString, value);
            return;
        }
#endif
        result[keyString] = CreateValue(target, keyString, value);
        _setByFile.Add((target.Index, keyString));
    }

    /// <summary>The value string; the previous load's string when the text did not change.</summary>
    private static string CreateValue(Target target, string key, ReadOnlySpan<char> value)
    {
        if (target.Previous != null
            && target.Previous.TryGetValue(key, out var previous)
            && previous.AsSpan().SequenceEqual(value))
            return previous;
        return value.ToString();
    }
}
