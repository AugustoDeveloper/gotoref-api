using System.IO.Compression;
using System.Reflection.Metadata;
using System.Xml.Linq;
using NuGet.Versioning;

namespace GotoRef.Api.Parsers;

/// <summary>
/// Maps assembly names referenced by a package's DLLs to their originating NuGet package ID and version.
/// Built from the .nuspec inside the .nupkg combined with the assembly references in the metadata.
/// </summary>
internal sealed class AssemblyPackageMap
{
    private static readonly HashSet<string> BclPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Private", "mscorlib", "netstandard", "System.Runtime",
        "System.Collections", "System.Linq", "System.Threading",
        "System.IO", "System.Net", "System.Text", "System.Reflection",
        "System.ComponentModel", "System.Diagnostics", "System.Globalization",
        "System.Resources", "System.Security", "System.Xml", "System.Console",
        "System.Memory", "System.Buffers", "System.Numerics",
        "System.ObjectModel", "System.Drawing", "System.Data",
        "WindowsBase", "PresentationCore", "PresentationFramework"
    };

    private readonly Dictionary<string, PackageRef> _map;
    private readonly HashSet<string> _currentAssemblies;

    public record PackageRef(string PackageId, string Version);

    private AssemblyPackageMap(Dictionary<string, PackageRef> map, HashSet<string> currentAssemblies)
    {
        _map = map;
        _currentAssemblies = currentAssemblies;
    }

    /// <summary>
    /// Resolves an assembly name to its originating NuGet package, or null if BCL / unknown.
    /// </summary>
    public PackageRef? Resolve(string assemblyName)
    {
        if (IsCurrentPackage(assemblyName)) return null;
        if (IsBcl(assemblyName)) return null;
        _map.TryGetValue(assemblyName, out var result);
        return result;
    }

    /// <summary>
    /// Returns true if the assembly belongs to the package being inspected.
    /// </summary>
    public bool IsCurrentPackage(string assemblyName)
        => _currentAssemblies.Contains(assemblyName);

    private static bool IsBcl(string assemblyName)
    {
        if (BclPrefixes.Contains(assemblyName)) return true;
        foreach (var prefix in BclPrefixes)
        {
            if (assemblyName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Builds the map from the .nupkg file and the DLLs' assembly references.
    /// </summary>
    public static AssemblyPackageMap Build(string nupkgPath, IReadOnlyList<string> dllPaths)
    {
        var currentAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in dllPaths)
        {
            var name = Path.GetFileNameWithoutExtension(dll);
            currentAssemblies.Add(name);
        }

        var dependencies = ParseNuspecDependencies(nupkgPath);
        var map = new Dictionary<string, PackageRef>(StringComparer.OrdinalIgnoreCase);

        // Direct match: assembly name == package ID
        // Collect all assembly references from all DLLs
        var allAssemblyRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in dllPaths)
        {
            try
            {
                using var stream = File.OpenRead(dll);
                using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);
                if (!peReader.HasMetadata) continue;
                var reader = peReader.GetMetadataReader();
                foreach (var asmRefHandle in reader.AssemblyReferences)
                {
                    var asmRef = reader.GetAssemblyReference(asmRefHandle);
                    allAssemblyRefs.Add(reader.GetString(asmRef.Name));
                }
            }
            catch { }
        }

        foreach (var (packageId, version) in dependencies)
        {
            // Direct match: package ID matches an assembly reference
            if (allAssemblyRefs.Contains(packageId))
            {
                map.TryAdd(packageId, new PackageRef(packageId, version));
            }
            else
            {
                // Try matching assembly refs that start with the package ID
                foreach (var asmRef in allAssemblyRefs)
                {
                    if (asmRef.StartsWith(packageId + ".", StringComparison.OrdinalIgnoreCase)
                        || packageId.StartsWith(asmRef + ".", StringComparison.OrdinalIgnoreCase))
                    {
                        map.TryAdd(asmRef, new PackageRef(packageId, version));
                    }
                }
            }
        }

        return new AssemblyPackageMap(map, currentAssemblies);
    }

    private static List<(string PackageId, string Version)> ParseNuspecDependencies(string nupkgPath)
    {
        var results = new List<(string, string)>();
        try
        {
            using var zip = ZipFile.OpenRead(nupkgPath);
            var nuspecEntry = zip.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
                && !e.FullName.Contains('/'));

            if (nuspecEntry is null) return results;

            using var stream = nuspecEntry.Open();
            var doc = XDocument.Load(stream);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            var groups = doc.Descendants(ns + "dependencies").Descendants(ns + "group");
            var bestGroup = PickBestFrameworkGroup(groups, ns);

            var deps = bestGroup is not null
                ? bestGroup.Elements(ns + "dependency")
                : doc.Descendants(ns + "dependencies").Elements(ns + "dependency");

            foreach (var dep in deps)
            {
                var id = dep.Attribute("id")?.Value;
                var versionRange = dep.Attribute("version")?.Value;
                if (id is null || versionRange is null) continue;

                var version = ResolveMinVersion(versionRange);
                if (version is not null)
                    results.Add((id, version));
            }
        }
        catch { }
        return results;
    }

    private static XElement? PickBestFrameworkGroup(IEnumerable<XElement> groups, XNamespace ns)
    {
        var preferred = new[] { "net8.0", "net7.0", "net6.0", "net5.0", ".NETStandard2.1", ".NETStandard2.0", ".NETStandard1.6", "net45" };
        var groupList = groups.ToList();

        foreach (var tfm in preferred)
        {
            var match = groupList.FirstOrDefault(g =>
            {
                var attr = g.Attribute("targetFramework")?.Value;
                return attr is not null && attr.Contains(tfm, StringComparison.OrdinalIgnoreCase);
            });
            if (match is not null) return match;
        }

        // Fallback: first group, or null (will use top-level dependencies)
        return groupList.FirstOrDefault();
    }

    private static string? ResolveMinVersion(string versionRange)
    {
        try
        {
            if (VersionRange.TryParse(versionRange, out var range))
                return range.MinVersion?.ToNormalizedString();
            if (NuGetVersion.TryParse(versionRange, out var exact))
                return exact.ToNormalizedString();
        }
        catch { }
        return versionRange; // return raw string as fallback
    }
}
