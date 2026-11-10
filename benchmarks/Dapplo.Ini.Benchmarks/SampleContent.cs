// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace Dapplo.Ini.Benchmarks;

/// <summary>Generates INI content of a given size, with the usual mix of comments, blank lines and values.</summary>
internal static class SampleContent
{
    public static string Create(int sections, int keysPerSection)
    {
        var sb = new StringBuilder();
        sb.AppendLine("; MyProduct settings, written by MyCompany tooling");
        for (var s = 0; s < sections; s++)
        {
            sb.AppendLine();
            sb.AppendLine($"; Settings of module {s}");
            sb.AppendLine($"[Module{s}]");
            for (var k = 0; k < keysPerSection; k++)
            {
                if (k % 5 == 0)
                    sb.AppendLine($"; The description of key {k}");
                switch (k % 4)
                {
                    case 0: sb.AppendLine($"Name{k} = Some text value number {k}"); break;
                    case 1: sb.AppendLine($"Count{k} = {k * 1234}"); break;
                    case 2: sb.AppendLine($"Enabled{k} = {(k % 3 == 0 ? "True" : "False")}"); break;
                    default: sb.AppendLine($"Items{k} = alpha,beta,gamma,delta,{k}"); break;
                }
            }
        }
        return sb.ToString();
    }
}
