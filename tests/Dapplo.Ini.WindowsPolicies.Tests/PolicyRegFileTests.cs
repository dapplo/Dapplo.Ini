// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.WindowsPolicies.Export;
using Microsoft.Win32;

namespace Dapplo.Ini.WindowsPolicies.Tests;

public sealed class PolicyRegFileTests
{
    private static CoreSettingsImpl Section()
    {
        var section = new CoreSettingsImpl();
        section.ResetToDefaults();
        return section;
    }

    [Fact]
    public void Mandatory_WritesTypedValues()
    {
        var section = Section();
        section.AppName = "Say \"hi\" \\ there";
        section.MaxRetries = 7;
        section.Plugins = new List<string> { "a", "b" };
        var content = PolicyRegFile.Create(new IIniSection[] { section }, "Dapplo", "PolicyTest");
        var lines = content.Split(new[] { "\r\n" }, StringSplitOptions.None);

        Assert.Equal(PolicyRegFile.Header, lines[0]);
        Assert.Contains(@"[HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Dapplo\PolicyTest\Core]", lines);
        Assert.Contains("\"CheckForUpdates\"=dword:00000001", lines);
        Assert.Contains("\"MaxRetries\"=dword:00000007", lines);
        Assert.Contains("\"AppName\"=\"Say \\\"hi\\\" \\\\ there\"", lines);
        Assert.Contains("\"OutputFileFormat\"=\"Png\"", lines);
        Assert.Contains("\"Threshold\"=\"1.5\"", lines);
        Assert.Contains("\"BigNumber\"=hex(b):00,00,00,00,00,00,00,00", lines);
        Assert.Contains(lines, l => l.StartsWith("\"OutputPath\"=hex(2):25,00,54,00", StringComparison.Ordinal));
        Assert.Contains(@"[-HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Dapplo\PolicyTest\Core\Plugins]", lines);
        Assert.Contains(@"[HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Dapplo\PolicyTest\Core\Plugins]", lines);
        Assert.Contains("\"1\"=\"a\"", lines);
        Assert.Contains("\"2\"=\"b\"", lines);
        Assert.Contains("\"x\"=\"1\"", lines);   // Headers dictionary

        Assert.DoesNotContain(lines, l => l.StartsWith("\"ApiToken\"", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("\"LastUsedFolder\"", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("\"SessionCount\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Recommended_OnlyUserValues_InHkcu()
    {
        var section = Section();
        section.MaxRetries = 3;
        var content = PolicyRegFile.Create(new IIniSection[] { section }, "Dapplo", "PolicyTest",
            new PolicyRegFileOptions { Kind = PolicyKind.Recommended, Hive = RegistryHive.CurrentUser, OnlyUserValues = true });
        Assert.Equal(PolicyRegFile.Header + "\r\n\r\n[HKEY_CURRENT_USER\\SOFTWARE\\Policies\\Dapplo\\PolicyTest\\Recommended\\Core]\r\n\"MaxRetries\"=dword:00000003\r\n",
            content);
    }

    [Fact]
    public void Write_IsUtf16WithBom()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".reg");
        try
        {
            PolicyRegFile.Write(path, new IIniSection[] { Section() }, "Dapplo", "PolicyTest");
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0xFE, bytes[1]);
            Assert.StartsWith(PolicyRegFile.Header, Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
