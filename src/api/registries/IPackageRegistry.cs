using GotoRef.Api.Domain.Models;

namespace GotoRef.Api.Registries;

/// <summary>
/// Contrato comum para registries de pacotes.
/// v1 → NuGetRegistry
/// v2 → NpmRegistry, v3 → PyPiRegistry, etc.
/// </summary>
public interface IPackageRegistry
{
    string RegistryName { get; }

    Task<List<PackageSearchResult>> SearchAsync(string query, int skip, int take, CancellationToken ct);
    Task<string[]> GetVersionsAsync(string packageId, CancellationToken ct);
    Task<string> DownloadPackageAsync(string packageId, string version, CancellationToken ct);
    Task<PackageRegistryMetadata> GetMetadataAsync(string packageId, string version, CancellationToken ct);
}
