using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using GotoRef.Api.Domain.Models;

namespace GotoRef.Api.Registries;

public class NuGetRegistry : IPackageRegistry
{
    public string RegistryName => "nuget";

    private readonly SourceRepository _repository;
    private readonly SourceCacheContext _cache;
    private readonly ILogger<NuGetRegistry> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _tempDir;

    public NuGetRegistry(ILogger<NuGetRegistry> logger, IHttpClientFactory httpClientFactory)
    {
        _repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
        _cache = new SourceCacheContext();
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _tempDir = Path.Combine(Path.GetTempPath(), "gotoref-packages");
        Directory.CreateDirectory(_tempDir);
    }

    public async Task<List<PackageSearchResult>> SearchAsync(string query, int skip, int take, CancellationToken ct)
    {
        var resource = await _repository.GetResourceAsync<PackageSearchResource>(ct);
        var filter = new SearchFilter(includePrerelease: false);
        var results = await resource.SearchAsync(query, filter, skip, take, NullLogger.Instance, ct);

        return results.Select(r => new PackageSearchResult(
            Id: r.Identity.Id,
            Version: r.Identity.Version.ToNormalizedString(),
            Description: r.Description ?? string.Empty,
            Authors: r.Authors,
            TotalDownloads: r.DownloadCount ?? 0,
            IconUrl: r.IconUrl?.ToString(),
            ProjectUrl: r.ProjectUrl?.ToString(),
            Tags: r.Tags?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? []
        )).ToList();
    }

    public async Task<string[]> GetVersionsAsync(string packageId, CancellationToken ct)
    {
        var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(ct);
        var versions = await resource.GetAllVersionsAsync(packageId, _cache, NullLogger.Instance, ct);
        return versions
            .OrderByDescending(v => v)
            .Select(v => v.ToNormalizedString())
            .Take(30)
            .ToArray();
    }

    public async Task<string> DownloadPackageAsync(string packageId, string version, CancellationToken ct)
    {
        var nupkgPath = Path.Combine(_tempDir, $"{packageId}.{version}.nupkg");
        if (File.Exists(nupkgPath))
        {
            _logger.LogInformation("Cache hit: {Id} {Version}", packageId, version);
            return nupkgPath;
        }

        var http = _httpClientFactory.CreateClient("nuget");
        var url = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLower()}/{version.ToLower()}/{packageId.ToLower()}.{version.ToLower()}.nupkg";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > 100 * 1024 * 1024)
            throw new InvalidOperationException("Package exceeds the 100MB limit.");

        await using var stream = new FileStream(nupkgPath, FileMode.Create, FileAccess.Write);
        await response.Content.CopyToAsync(stream, cts.Token);

        _logger.LogInformation("Downloaded {Id} {Version}", packageId, version);
        return nupkgPath;
    }

    public async Task<PackageRegistryMetadata> GetMetadataAsync(string packageId, string version, CancellationToken ct)
    {
        var resource = await _repository.GetResourceAsync<PackageMetadataResource>(ct);
        var nugetVersion = NuGetVersion.Parse(version);
        var metadata = await resource.GetMetadataAsync(
            new NuGet.Packaging.Core.PackageIdentity(packageId, nugetVersion),
            _cache, NullLogger.Instance, ct);

        if (metadata is null)
            return new PackageRegistryMetadata(0, [], default, default, default, default, default, default, default);

        var frameworks = metadata.DependencySets
            .Select(d => d.TargetFramework.GetShortFolderName())
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct()
            .ToArray();

        return new PackageRegistryMetadata(
            TotalDownloads: metadata.DownloadCount ?? 0,
            TargetFrameworks: frameworks,
            LicenseExpression: metadata.LicenseMetadata?.License,
            LicenseUrl: metadata.LicenseUrl?.ToString(),
            ProjectUrl: metadata.ProjectUrl?.ToString(),
            RepositoryUrl: null,
            RepositoryType: null,
            CommitHash: null,
            PublishedAt: metadata.Published
        );
    }
}
