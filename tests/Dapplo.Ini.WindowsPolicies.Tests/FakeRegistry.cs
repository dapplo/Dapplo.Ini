// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.WindowsPolicies.Registry;
using Dapplo.Ini.WindowsPolicies.Watching;
using Microsoft.Win32;

namespace Dapplo.Ini.WindowsPolicies.Tests;

/// <summary>An in-memory registry hive.</summary>
internal sealed class FakeRegistry : IRegistryRoot
{
    private readonly FakeKey _root = new();

    public IRegistryKeyReader? OpenKey(string path)
    {
        var key = _root;
        foreach (var part in path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!key.SubKeys.TryGetValue(part, out key!))
                return null;
        }
        return new Reader(key);
    }

    public FakeRegistry Set(string path, string name, object value, RegistryValueKind kind)
    {
        var key = _root;
        foreach (var part in path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!key.SubKeys.TryGetValue(part, out var sub))
                key.SubKeys[part] = sub = new FakeKey();
            key = sub;
        }
        key.Values[name] = (value, kind);
        return this;
    }

    public void DeleteKey(string path)
    {
        var parts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var key = _root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!key.SubKeys.TryGetValue(parts[i], out key!))
                return;
        }
        key.SubKeys.Remove(parts[^1]);
    }

    private sealed class FakeKey
    {
        public Dictionary<string, FakeKey> SubKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, (object Value, RegistryValueKind Kind)> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Reader : IRegistryKeyReader
    {
        private readonly FakeKey _key;

        public Reader(FakeKey key) => _key = key;

        public string[] GetValueNames() => _key.Values.Keys.ToArray();

        public bool TryGetValue(string name, out object? value, out RegistryValueKind kind)
        {
            if (_key.Values.TryGetValue(name, out var entry))
            {
                value = entry.Value;
                kind = entry.Kind;
                return true;
            }
            value = null;
            kind = RegistryValueKind.None;
            return false;
        }

        public IRegistryKeyReader? OpenSubKey(string name) => _key.SubKeys.TryGetValue(name, out var sub) ? new Reader(sub) : null;

        public void Dispose()
        {
        }
    }
}

/// <summary>A watcher the test fires by hand.</summary>
internal sealed class FakeWatcher : IPolicyChangeWatcher
{
    public event EventHandler? Changed;

    public bool IsDisposed { get; private set; }

    public void Fire() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose() => IsDisposed = true;
}
