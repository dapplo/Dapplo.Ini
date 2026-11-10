// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reflection;
using System.Runtime.Loader;
using Dapplo.Ini.Configuration;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.WindowsPolicies;
using Dapplo.Ini.WindowsPolicies.Admx;
using Dapplo.Ini.WindowsPolicies.Export;
using Microsoft.Win32;

namespace Dapplo.Ini.WindowsPolicies.Tool;

/// <summary>
/// <c>dapplo-ini-admx --assembly MyApp.dll --vendor MyCompany --product MyProduct --output policies</c>
/// </summary>
internal static class Program
{
    private const string Usage = """
        Generates an ADMX/ADML administrative template for the Dapplo.Ini sections of an assembly.

        dapplo-ini-admx --assembly <path> --vendor <vendor> --product <product> [options]

          --assembly <path>       the application assembly (or a library) with the generated sections; repeatable
          --vendor <name>         the vendor, as passed to AddWindowsPolicies
          --product <name>        the product, as passed to AddWindowsPolicies
          --output <directory>    where to write <prefix>.admx and <lang>\<prefix>.adml (default: current directory)
          --prefix <prefix>       namespace prefix and file name (default: the product, lower case)
          --namespace <ns>        target namespace (default: <Vendor>.Policies.<Product>)
          --revision <x.y>        template revision (default: 1.0)
          --language <lang>       an ADML language (default: en-US); repeatable
          --supported-on <text>   the "Supported on" text (default: the product)
          --root-path <path>      policy root below the hive (default: SOFTWARE\Policies\<Vendor>\<Product>)
          --section <name>        only this section; repeatable
          --no-recommended        no recommended (user can override) policies
          --reg <file>            also write a .reg file with the compiled defaults (mandatory, HKLM)
        """;

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var assemblies = new List<string>();
        var languages = new List<string>();
        var sectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? vendor = null, product = null, output = null, prefix = null, ns = null, revision = null, supportedOn = null,
            rootPath = null, reg = null;
        var recommended = true;

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (args[i])
            {
                case "--assembly": assemblies.Add(Next()); break;
                case "--vendor": vendor = Next(); break;
                case "--product": product = Next(); break;
                case "--output": output = Next(); break;
                case "--prefix": prefix = Next(); break;
                case "--namespace": ns = Next(); break;
                case "--revision": revision = Next(); break;
                case "--language": languages.Add(Next()); break;
                case "--supported-on": supportedOn = Next(); break;
                case "--root-path": rootPath = Next(); break;
                case "--section": sectionNames.Add(Next()); break;
                case "--no-recommended": recommended = false; break;
                case "--reg": reg = Next(); break;
                case "-h" or "--help" or "-?":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }
        if (assemblies.Count == 0)
            throw new ArgumentException("--assembly is needed.");
        if (string.IsNullOrWhiteSpace(vendor) || string.IsNullOrWhiteSpace(product))
            throw new ArgumentException("--vendor and --product are needed.");

        var sections = new List<IIniSection>();
        foreach (var path in assemblies)
            sections.AddRange(LoadSections(Path.GetFullPath(path)));
        if (sectionNames.Count > 0)
            sections = sections.Where(s => sectionNames.Contains(s.SectionName)).ToList();
        if (sections.Count == 0)
        {
            Console.Error.WriteLine("error: no Dapplo.Ini sections found.");
            return 1;
        }

        var options = new AdmxOptions(vendor!, product!)
        {
            Prefix = prefix,
            Namespace = ns,
            SupportedOn = supportedOn,
            RootPath = rootPath,
            IncludeRecommended = recommended
        };
        if (revision != null)
            options.Revision = revision;
        if (languages.Count > 0)
            options.Languages = languages;

        var template = AdmxGenerator.Generate(sections, options);
        foreach (var file in template.WriteTo(output ?? Directory.GetCurrentDirectory()))
            Console.WriteLine(file);
        Console.WriteLine($"{template.PolicyCount} policies in {sections.Count} sections.");

        if (reg != null)
        {
            PolicyRegFile.Write(reg, sections, vendor!, product!, new PolicyRegFileOptions { RootPath = rootPath, Hive = RegistryHive.LocalMachine });
            Console.WriteLine(Path.GetFullPath(reg));
        }
        return 0;
    }

    /// <summary>
    /// Loads the assembly in its own load context (with its dependencies), sharing Dapplo.Ini with the tool so the
    /// sections are <see cref="IIniSection"/>s here, and creates every generated section with its defaults.
    /// </summary>
    private static IEnumerable<IIniSection> LoadSections(string path)
    {
        if (!File.Exists(path))
            throw new ArgumentException($"The assembly '{path}' doesn't exist.");
        var context = new AppLoadContext(path);
        var assembly = context.LoadFromAssemblyPath(path);
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t != null).ToArray()!;
        }
        foreach (var type in types.Where(t => !t.IsAbstract && typeof(IniSectionBase).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var section = (IniSectionBase)Activator.CreateInstance(type)!;
            section.ResetToDefaults();
            yield return section;
        }
    }

    private sealed class AppLoadContext : AssemblyLoadContext
    {
        private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
        {
            "Dapplo.Ini", "Dapplo.Ini.WindowsPolicies"
        };

        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _directory;

        public AppLoadContext(string mainAssemblyPath) : base("dapplo-ini-admx", isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
            _directory = Path.GetDirectoryName(mainAssemblyPath)!;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name != null && Shared.Contains(assemblyName.Name))
                return null;   // the tool's copy
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path == null && assemblyName.Name != null)
            {
                var candidate = Path.Combine(_directory, assemblyName.Name + ".dll");
                if (File.Exists(candidate))
                    path = candidate;
            }
            return path != null ? LoadFromAssemblyPath(path) : null;
        }
    }
}
