// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Dapplo.Ini.WindowsPolicies.Watching;

/// <summary>
/// Watches a policy root with <c>RegNotifyChangeKeyValue</c>: the root with its sub tree (values and keys) and the
/// nearest existing parent (so the creation or deletion of the root is noticed too). One background thread per
/// hive, view and root, shared by all sources of it (mandatory and recommended) and reference counted.
/// On anything but Windows it reports nothing.
/// </summary>
internal static class RegistryChangeWatcher
{
    private static readonly Dictionary<string, SharedWatcher> Watchers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Starts (or joins) watching; dispose the result to leave.</summary>
    public static IPolicyChangeWatcher Acquire(RegistryHive hive, RegistryView view, string rootPath)
    {
        var id = $"{hive}|{view}|{rootPath.Trim('\\')}";
        SharedWatcher shared;
        lock (Watchers)
        {
            if (!Watchers.TryGetValue(id, out shared!))
            {
                shared = new SharedWatcher(id, hive, view, rootPath.Trim('\\'));
                Watchers.Add(id, shared);
            }
            shared.References++;
            if (shared.References == 1)
                shared.Start();
        }
        return new Lease(shared);
    }

    private static void Release(SharedWatcher shared)
    {
        lock (Watchers)
        {
            if (--shared.References > 0)
                return;
            Watchers.Remove(shared.Id);
        }
        shared.Stop();
    }

    internal static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    private sealed class Lease : IPolicyChangeWatcher
    {
        private SharedWatcher? _shared;

        public Lease(SharedWatcher shared)
        {
            _shared = shared;
            shared.Changed += OnChanged;
        }

        public event EventHandler? Changed;

        private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
            var shared = Interlocked.Exchange(ref _shared, null);
            if (shared == null)
                return;
            shared.Changed -= OnChanged;
            Release(shared);
        }
    }

    private sealed class SharedWatcher
    {
        private const int RegNotifyChangeName = 0x1;
        private const int RegNotifyChangeLastSet = 0x4;

        private readonly RegistryHive _hive;
        private readonly RegistryView _view;
        private readonly string _rootPath;
        private ManualResetEvent? _stop;
        private Thread? _thread;

        public SharedWatcher(string id, RegistryHive hive, RegistryView view, string rootPath)
        {
            Id = id;
            _hive = hive;
            _view = view;
            _rootPath = rootPath;
        }

        public string Id { get; }

        public int References { get; set; }

        public event EventHandler? Changed;

        public void Start()
        {
            if (!IsWindows)
                return;
            _stop = new ManualResetEvent(false);
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "Dapplo.Ini policy watcher " + _rootPath
            };
            _thread.Start(_stop);
        }

        public void Stop()
        {
            var stop = _stop;
            var thread = _thread;
            _stop = null;
            _thread = null;
            if (stop == null)
                return;
            stop.Set();
            if (thread != null && thread != Thread.CurrentThread)
                thread.Join(TimeSpan.FromSeconds(5));
            stop.Dispose();
        }

        private void Run(object? state)
        {
            var stop = (ManualResetEvent)state!;
            while (true)
            {
                var keys = new List<RegistryKey>();
                var events = new List<AutoResetEvent>();
                try
                {
                    try
                    {
                        Register(keys, events);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        // Retry below, after a pause
                    }

                    if (events.Count == 0)
                    {
                        // Nothing to watch (no access): look again later
                        if (stop.WaitOne(TimeSpan.FromSeconds(30)))
                            return;
                        continue;
                    }

                    var handles = new WaitHandle[events.Count + 1];
                    handles[0] = stop;
                    for (var i = 0; i < events.Count; i++)
                        handles[i + 1] = events[i];
                    if (WaitHandle.WaitAny(handles) == 0)
                        return;
                    try
                    {
                        Changed?.Invoke(this, EventArgs.Empty);
                    }
                    catch
                    {
                        // A failing listener must not stop the watching.
                    }
                }
                finally
                {
                    foreach (var key in keys)
                        key.Dispose();
                    foreach (var ev in events)
                        ev.Dispose();
                }
            }
        }

        /// <summary>Registers the root (sub tree) when it exists, and the nearest existing parent (its direct sub keys).</summary>
        private void Register(List<RegistryKey> keys, List<AutoResetEvent> events)
        {
            using var baseKey = RegistryKey.OpenBaseKey(_hive, _view);
            var root = baseKey.OpenSubKey(_rootPath, writable: false);
            if (root != null)
                Watch(root, watchSubtree: true, keys, events);

            var path = _rootPath;
            while (true)
            {
                var separator = path.LastIndexOf('\\');
                var parentPath = separator < 0 ? "" : path.Substring(0, separator);
                var parent = parentPath.Length == 0 ? baseKey.OpenSubKey("", writable: false) : baseKey.OpenSubKey(parentPath, writable: false);
                if (parent != null)
                {
                    Watch(parent, watchSubtree: false, keys, events);
                    return;
                }
                if (parentPath.Length == 0)
                    return;
                path = parentPath;
            }
        }

        private static void Watch(RegistryKey key, bool watchSubtree, List<RegistryKey> keys, List<AutoResetEvent> events)
        {
            keys.Add(key);
            var ev = new AutoResetEvent(false);
            var result = RegNotifyChangeKeyValue(key.Handle, watchSubtree, RegNotifyChangeName | RegNotifyChangeLastSet,
                ev.SafeWaitHandle, fAsynchronous: true);
            if (result == 0)
                events.Add(ev);
            else
                ev.Dispose();
        }

        [DllImport("advapi32.dll", SetLastError = false)]
        private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle hKey, bool bWatchSubtree, int dwNotifyFilter,
            SafeWaitHandle hEvent, bool fAsynchronous);
    }
}
