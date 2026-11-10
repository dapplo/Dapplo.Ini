// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Interfaces;
using Microsoft.Win32;

namespace Dapplo.Ini.WindowsPolicies.Tests;

/// <summary>
/// The real registry and RegNotifyChangeKeyValue, below HKCU\SOFTWARE\Dapplo.Ini.Tests (HKCU\SOFTWARE\Policies is
/// read-only for normal users). These tests do nothing on other platforms.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class RealRegistryTests : IDisposable
{
    private readonly string _root = @"SOFTWARE\Dapplo.Ini.Tests\" + Guid.NewGuid().ToString("N");

    private static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose()
    {
        if (IsWindows)
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
    }

    [Fact]
    public void ReadsTheRegistry()
    {
        if (!IsWindows)
            return;
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_root + @"\Core"))
        {
            key.SetValue("MaxRetries", 4, RegistryValueKind.DWord);
            key.SetValue("OutputPath", @"%TEMP%\x", RegistryValueKind.ExpandString);
            key.SetValue("Plugins", new[] { "p1", "p2" }, RegistryValueKind.MultiString);
        }
        using var source = new WindowsPolicySource(RegistryHive.CurrentUser, PolicyKind.Mandatory, _root, RegistryView.Registry64, watchForChanges: false);
        var section = new CoreSettingsImpl();
        section.ResetToDefaults();
        Assert.True(source.TryGetValue(section, "MaxRetries", out var retries));
        Assert.Equal("4", retries);
        Assert.True(source.TryGetValue(section, "OutputPath", out var path));
        Assert.Equal(@"%TEMP%\x", path);
        Assert.True(source.TryGetValue(section, "Plugins", out var plugins));
        Assert.Equal("p1,p2", plugins);
        Assert.False(source.TryGetValue(section, "AppName", out _));
    }

    [Fact]
    public async Task Watcher_ReportsCreationAndChangesOfTheRoot()
    {
        if (!IsWindows)
            return;
        using var source = new WindowsPolicySource(RegistryHive.CurrentUser, PolicyKind.Mandatory, _root, RegistryView.Registry64,
            watchForChanges: true, changeDebounce: TimeSpan.FromMilliseconds(100));
        var raised = 0;
        source.ValueChanged += (_, _) => Interlocked.Increment(ref raised);
        await Task.Delay(200);   // let the watcher register

        // The root doesn't exist yet: its creation is noticed through the parent
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_root + @"\Core"))
            key.SetValue("MaxRetries", 1, RegistryValueKind.DWord);
        await WaitFor(() => Volatile.Read(ref raised) >= 1);
        await Task.Delay(500);
        var afterCreate = Volatile.Read(ref raised);

        // A burst of changes below the root: one event
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_root + @"\Core"))
        {
            for (var i = 0; i < 10; i++)
                key.SetValue("MaxRetries", i, RegistryValueKind.DWord);
        }
        await WaitFor(() => Volatile.Read(ref raised) > afterCreate);
        await Task.Delay(500);
        Assert.Equal(afterCreate + 1, Volatile.Read(ref raised));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(20);
        }
    }
}
