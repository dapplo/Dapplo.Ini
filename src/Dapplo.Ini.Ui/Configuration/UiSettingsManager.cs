// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Ui.Interfaces;

namespace Dapplo.Ini.Ui.Configuration;

/// <summary>
/// A lightweight implementation of <see cref="IUiSettingsManager"/> that delegates
/// apply/rollback behaviour to the <see cref="ITransactional"/> interface already built
/// into Dapplo.Ini sections.
/// </summary>
/// <remarks>
/// <para>
/// Register every <c>IIniSection</c> that participates in the settings UI by calling
/// <see cref="RegisterSection"/> before opening the settings window.
/// </para>
/// <para>
/// If a section does <em>not</em> implement <see cref="ITransactional"/> it is still
/// tracked for change detection via <see cref="IIniSection.HasChanges"/> but changes
/// are applied immediately (no buffering).
/// </para>
/// </remarks>
public sealed class UiSettingsManager : IUiSettingsManager
{
    private readonly List<IIniSection> _sections = new();
    private bool _sessionActive;

    /// <inheritdoc />
    public bool HasPendingChanges =>
        _sections.Any(s => s is ITransactional t ? t.IsInTransaction && s.HasChanges : s.HasChanges);

    /// <summary>
    /// Registers an INI section with this manager so that its changes are included in
    /// the apply/rollback cycle.
    /// </summary>
    /// <param name="section">The section to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="section"/> is <c>null</c>.</exception>
    public void RegisterSection(IIniSection section)
    {
        if (section is null) throw new ArgumentNullException(nameof(section));
        if (!_sections.Contains(section))
            _sections.Add(section);
    }

    /// <inheritdoc />
    public void BeginSession()
    {
        _sessionActive = true;
        foreach (var section in _sections)
        {
            if (section is ITransactional t)
                t.Begin();
        }
    }

    /// <inheritdoc />
    public void Apply()
    {
        foreach (var section in _sections)
        {
            if (section is ITransactional t)
                t.Commit();
        }
    }

    /// <inheritdoc />
    public void Rollback()
    {
        foreach (var section in _sections)
        {
            if (section is ITransactional t)
                t.Rollback();
        }
    }

    /// <inheritdoc />
    public void EndSession()
    {
        if (!_sessionActive) return;
        if (HasPendingChanges)
            Rollback();
        _sessionActive = false;
    }
}
