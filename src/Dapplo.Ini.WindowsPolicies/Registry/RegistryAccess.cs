// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Win32;

namespace Dapplo.Ini.WindowsPolicies.Registry;

/// <summary>Read access to one registry hive (and view); abstracted so the mapping can be tested without a registry.</summary>
internal interface IRegistryRoot
{
    /// <summary>Opens <paramref name="path"/> for reading, or returns <c>null</c> when it doesn't exist.</summary>
    IRegistryKeyReader? OpenKey(string path);
}

/// <summary>An open registry key.</summary>
internal interface IRegistryKeyReader : IDisposable
{
    string[] GetValueNames();

    /// <summary>Reads a value without expanding environment variables.</summary>
    bool TryGetValue(string name, out object? value, out RegistryValueKind kind);

    IRegistryKeyReader? OpenSubKey(string name);
}

/// <summary>The real registry.</summary>
internal sealed class WindowsRegistryRoot : IRegistryRoot
{
    private readonly RegistryHive _hive;
    private readonly RegistryView _view;

    public WindowsRegistryRoot(RegistryHive hive, RegistryView view)
    {
        _hive = hive;
        _view = view;
    }

    public IRegistryKeyReader? OpenKey(string path)
    {
        using var baseKey = RegistryKey.OpenBaseKey(_hive, _view);
        var key = baseKey.OpenSubKey(path, writable: false);
        return key == null ? null : new WindowsRegistryKeyReader(key);
    }
}

internal sealed class WindowsRegistryKeyReader : IRegistryKeyReader
{
    private readonly RegistryKey _key;

    public WindowsRegistryKeyReader(RegistryKey key) => _key = key;

    public string[] GetValueNames() => _key.GetValueNames();

    public bool TryGetValue(string name, out object? value, out RegistryValueKind kind)
    {
        value = _key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value == null)
        {
            kind = RegistryValueKind.None;
            return false;
        }
        kind = _key.GetValueKind(name);
        return true;
    }

    public IRegistryKeyReader? OpenSubKey(string name)
    {
        var key = _key.OpenSubKey(name, writable: false);
        return key == null ? null : new WindowsRegistryKeyReader(key);
    }

    public void Dispose() => _key.Dispose();
}
