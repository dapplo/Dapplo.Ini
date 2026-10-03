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
    // Never modified after it is published: UpdateTranslations swaps in a new dictionary, so UI threads
    // reading translations during a language switch or file reload always see a complete set.
    private volatile Dictionary<string, string> _translations =
        new(StringComparer.OrdinalIgnoreCase);

    // ── ILanguageSection ──────────────────────────────────────────────────────

    /// <summary>
    /// The <c>[SectionName]</c> header used to locate translations in the language file.
    /// Set by the source generator from <see cref="Attributes.IniLanguageSectionAttribute.SectionName"/>
    /// (or derived from the interface name when no explicit name is given).
    /// </summary>
    public abstract string SectionName { get; }

    /// <summary>
    /// Optional module name used in the file naming convention.
    /// When non-<c>null</c> the loader reads from <c>{basename}.{moduleName}.{ietf}.ini</c>;
    /// when <c>null</c> the loader reads from <c>{basename}.{ietf}.ini</c>.
    /// </summary>
    public abstract string? ModuleName { get; }

    // ── Translation updates ───────────────────────────────────────────────────

    /// <summary>
    /// Atomically updates all translations from <paramref name="newTranslations"/>.
    /// Derived classes override this to detect property value changes and raise
    /// property-change notifications.
    /// </summary>
    /// <param name="newTranslations">The new dictionary of normalized key to translated value.</param>
    public virtual void UpdateTranslations(IReadOnlyDictionary<string, string> newTranslations)
    {
        // A dictionary built by the LanguageConfig is never changed after it was handed over: use it as is
        // instead of copying it (the generated override passes the same instance on to this method).
        if (ReferenceEquals(newTranslations, _handOver))
        {
            _translations = _handOver;
            return;
        }

        var updated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in newTranslations)
        {
            updated[kvp.Key] = kvp.Value;
        }
        _translations = updated;
    }

    /// <summary>
    /// Stores a single translated value. The <paramref name="normalizedKey"/> must already
    /// be normalized (trimmed, lowercase, underscores and dashes removed).
    /// </summary>
    public void SetTranslation(string normalizedKey, string value)
    {
        var copy = new Dictionary<string, string>(_translations, StringComparer.OrdinalIgnoreCase)
        {
            [normalizedKey] = value
        };
        UpdateTranslations(copy);
    }

    /// <summary>Removes all currently loaded translations.</summary>
    public void ClearTranslations()
        => UpdateTranslations(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

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

    /// <summary>
    /// Returns the translated value for <paramref name="normalizedKey"/> from the specified
    /// <paramref name="dictionary"/>, or the sentinel string <c>###<paramref name="propertyName"/>###</c>
    /// when the key is not found.
    /// </summary>
    /// <param name="dictionary">The translation dictionary to look in.</param>
    /// <param name="normalizedKey">The key after normalization (lowercase, no _ or -).</param>
    /// <param name="propertyName">The C# property name, used for the sentinel fallback.</param>
    protected static string GetTranslation(IReadOnlyDictionary<string, string> dictionary, string normalizedKey, string propertyName)
        => dictionary.TryGetValue(normalizedKey, out var value) ? value : $"###{propertyName}###";

    // ── Key normalization ─────────────────────────────────────────────────────

    /// <summary>
    /// Normalizes a key according to the language pack rules:
    /// trim whitespace, remove <c>_</c> and <c>-</c>, convert to lower-case.
    /// </summary>
    public static string NormalizeKey(string key)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (key.Length <= MaxStackKeyLength)
        {
            Span<char> buffer = stackalloc char[key.Length];
            var length = NormalizeKey(key.AsSpan(), buffer);
            return buffer.Slice(0, length).ToString();
        }

        var pooled = System.Buffers.ArrayPool<char>.Shared.Rent(key.Length);
        try
        {
            var length = NormalizeKey(key.AsSpan(), pooled);
            return new string(pooled, 0, length);
        }
        finally
        {
            System.Buffers.ArrayPool<char>.Shared.Return(pooled);
        }
    }

    /// <summary>Keys up to this length are normalized in a stack buffer.</summary>
    internal const int MaxStackKeyLength = 256;

    /// <summary>
    /// Normalizes <paramref name="key"/> into <paramref name="destination"/> (at least as long as the key) without
    /// allocating: trims whitespace, removes <c>_</c> and <c>-</c>, lower-cases. Returns the length written.
    /// </summary>
    internal static int NormalizeKey(ReadOnlySpan<char> key, Span<char> destination)
    {
        key = key.Trim();
        var length = 0;
        foreach (var ch in key)
        {
            if (ch != '_' && ch != '-')
                destination[length++] = char.ToLowerInvariant(ch);
        }
        return length;
    }

    /// <summary>
    /// <c>true</c> when looking up <paramref name="key"/> in the (case-insensitive) translations needs no
    /// normalization: no <c>_</c> or <c>-</c> and no surrounding whitespace.
    /// </summary>
    private static bool IsLookupReady(string key)
        => key.Length > 0
           && !char.IsWhiteSpace(key[0]) && !char.IsWhiteSpace(key[key.Length - 1])
           && key.IndexOf('_') < 0 && key.IndexOf('-') < 0;

    /// <summary>
    /// Looks up a key as given by a caller (normalized first). Allocates nothing for keys without <c>_</c>, <c>-</c>
    /// or surrounding whitespace; on .NET 9+ never.
    /// </summary>
    private bool TryGetByKey(string key, out string? value)
    {
        var translations = _translations;
        if (IsLookupReady(key))
            return TryGet(translations, key, out value);
#if NET9_0_OR_GREATER
        return TryGetByKey(key.AsSpan(), out value);
#else
        return TryGet(translations, NormalizeKey(key), out value);
#endif
    }

    /// <summary>Looks up a key as given by a caller, from a span (e.g. a part of <c>module.key</c>).</summary>
    internal bool TryGetByKey(ReadOnlySpan<char> key, out string? value)
    {
        if (key.Length > MaxStackKeyLength)
            return TryGet(_translations, NormalizeKey(key.ToString()), out value);
        Span<char> buffer = stackalloc char[key.Length];
        var length = NormalizeKey(key, buffer);
        return TryGetNormalized(buffer.Slice(0, length), out value);
    }

    /// <summary>Looks up an already normalized key; allocation-free on .NET 9+.</summary>
    internal bool TryGetNormalized(ReadOnlySpan<char> normalizedKey, out string? value)
    {
#if NET9_0_OR_GREATER
        if (_translations.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(normalizedKey, out var found))
        {
            value = found;
            return true;
        }
        value = null;
        return false;
#else
        return TryGet(_translations, normalizedKey.ToString(), out value);
#endif
    }

    private static bool TryGet(Dictionary<string, string> translations, string key, out string? value)
    {
        if (translations.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }
        value = null;
        return false;
    }

    // Set by ApplyBuiltTranslations for the duration of one UpdateTranslations call (one applier per section).
    private Dictionary<string, string>? _handOver;

    /// <summary>
    /// Applies translations built by a <see cref="LanguageConfig"/> (a new case-insensitive dictionary that is never
    /// changed afterwards) through <see cref="UpdateTranslations"/>, without copying them.
    /// </summary>
    internal void ApplyBuiltTranslations(Dictionary<string, string> translations)
    {
        _handOver = translations;
        try
        {
            UpdateTranslations(translations);
        }
        finally
        {
            _handOver = null;
        }
    }

    /// <summary>The current translations (never modified after being published), for building the next set.</summary>
    internal Dictionary<string, string> CurrentTranslations => _translations;

    // ── IReadOnlyDictionary<string, string> ──────────────────────────────────

    /// <summary>
    /// Returns the translated value for the given key (normalized before lookup).
    /// Returns <c>###key###</c> when the key is not found.
    /// </summary>
    public string this[string key] => TryGetByKey(key, out var value) ? value! : $"###{key}###";

    /// <summary>
    /// Formats the translation for <paramref name="key"/> using the supplied arguments. Never throws:
    /// a missing key returns the <c>###key###</c> sentinel, a translation that cannot be formatted (bad format
    /// string, too few arguments) is returned unformatted. Both are reported via
    /// <see cref="Interfaces.ILanguageConfigListener"/> of the <see cref="LanguageConfig"/> the section is registered with.
    /// </summary>
    public string Format(string key, params object[] args)
    {
        if (key is null || !TryGetByKey(key, out var format))
        {
            Owner?.ReportTranslationNotFound(SectionName, key ?? "");
            return $"###{key}###";
        }

        try
        {
            return string.Format(format!, args);
        }
        catch (Exception ex)
        {
            Owner?.ReportFormatFailed(SectionName, key, ex);
            return format!;
        }
    }

    /// <summary>The configuration this section is registered with (for diagnostics), or <c>null</c>.</summary>
    internal LanguageConfig? Owner { get; set; }


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
        => TryGetByKey(key, out _);

    /// <inheritdoc/>
    public bool TryGetValue(string key, out string value)
    {
        var found = TryGetByKey(key, out var translation);
        value = translation!;
        return found;
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        => _translations.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        => _translations.GetEnumerator();
}
