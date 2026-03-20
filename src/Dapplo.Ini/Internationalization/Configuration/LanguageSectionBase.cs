// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Internationalization.Interfaces;

namespace Dapplo.Ini.Internationalization.Configuration;

/// <summary>
/// Base class for all source-generated language section classes.
/// Holds a dictionary of normalized-key → translated-value and provides
/// helper methods for generated property getters.
/// Also implements <see cref="IReadOnlyDictionary{TKey,TValue}"/> so that
/// consumer interfaces that extend <c>IReadOnlyDictionary&lt;string, string&gt;</c>
/// are automatically satisfied.
/// </summary>
public abstract class LanguageSectionBase : ILanguageSection, IReadOnlyDictionary<string, string>
{
    // Translations keyed by normalized key (lowercase, no underscores/dashes).
    private readonly Dictionary<string, string> _translations =
        new(StringComparer.OrdinalIgnoreCase);

    // ── ILanguageSection ──────────────────────────────────────────────────────

    /// <summary>
    /// Optional section name used for file naming and section routing within a
    /// full <c>.ini</c> file.
    /// <list type="bullet">
    ///   <item>Dedicated file: <c>{basename}.{sectionName}.{ietf}.ini</c></item>
    ///   <item>Fallback: <c>[sectionName]</c> section in <c>{basename}.{ietf}.ini</c></item>
    /// </list>
    /// Returns <c>null</c> when the section reads from the main file (no section filter).
    /// </summary>
    public abstract string? SectionName { get; }

    /// <summary>
    /// Obsolete: use <see cref="SectionName"/> instead.
    /// </summary>
    [Obsolete("Use SectionName instead.")]
    public string? ModuleName => SectionName;

    // ── Internal helpers used by LanguageConfig ───────────────────────────────

    /// <summary>
    /// Stores a single translated value. The <paramref name="normalizedKey"/> must already
    /// be normalized (trimmed, lowercase, underscores and dashes removed).
    /// </summary>
    internal void SetTranslation(string normalizedKey, string value)
        => _translations[normalizedKey] = value;

    /// <summary>Removes all currently loaded translations.</summary>
    internal void ClearTranslations() => _translations.Clear();

    // ── Helper used by generated property getters ─────────────────────────────

    /// <summary>
    /// Returns the translated value for <paramref name="normalizedKey"/>, or the sentinel
    /// string <c>###<paramref name="propertyName"/>###</c> when the key is not found.
    /// </summary>
    /// <param name="normalizedKey">The key after normalization (lowercase, no _ or -).</param>
    /// <param name="propertyName">
    /// The C# property name, used both for the sentinel fallback and for the
    /// <c>nameof(...)</c> in generated code.
    /// </param>
    protected string GetTranslation(string normalizedKey, string propertyName)
        => _translations.TryGetValue(normalizedKey, out var value) ? value : $"###{propertyName}###";

    // ── Key normalization ─────────────────────────────────────────────────────

    /// <summary>
    /// Normalizes a key according to the language pack rules:
    /// trim whitespace, remove <c>_</c> and <c>-</c>, convert to lower-case.
    /// </summary>
    public static string NormalizeKey(string key)
    {
        var span = key.AsSpan().Trim();
        var sb = new System.Text.StringBuilder(span.Length);
        foreach (var ch in span)
        {
            if (ch != '_' && ch != '-')
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    // ── IReadOnlyDictionary<string, string> ──────────────────────────────────

    /// <summary>
    /// Returns the translated value for the given key (normalized before lookup).
    /// Returns <c>###key###</c> when the key is not found.
    /// </summary>
    public string this[string key] => GetTranslation(NormalizeKey(key), key);

    /// <inheritdoc/>
    public IEnumerable<string> Keys
        => _translations.Keys;

    /// <inheritdoc/>
    public IEnumerable<string> Values
        => _translations.Values;

    /// <inheritdoc/>
    public int Count => _translations.Count;

    /// <inheritdoc/>
    public bool ContainsKey(string key)
        => _translations.ContainsKey(NormalizeKey(key));

    /// <inheritdoc/>
    public bool TryGetValue(string key, out string value)
        => _translations.TryGetValue(NormalizeKey(key), out value!);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        => _translations.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        => _translations.GetEnumerator();
}

