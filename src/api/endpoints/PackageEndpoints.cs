using GotoRef.Api.Domain.Models;
using GotoRef.Api.Domain.Services;
using GotoRef.Api.Parsers;
using GotoRef.Api.Registries;
using System.Text.RegularExpressions;

namespace GotoRef.Api.Endpoints;
 
public static class PackageEndpoints
{
    private static readonly Regex ValidId = new(@"^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

    public static void MapPackageEndpoints(this WebApplication app)
    {
        // GET /api/packages/search?q=newtonsoft&take=10
        app.MapGet("/api/packages/search", async (
            string q,
            int skip = 0,
            int take = 10,
            IPackageRegistry registry = default!,
            CacheService cache = default!,
            CancellationToken ct = default) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new { error = "Query parameter 'q' is required." });

            take = Math.Clamp(take, 1, 50);
            skip = Math.Max(skip, 0);

            var cacheKey = $"search:{q.ToLower()}:{skip}:{take}";
            var results = await cache.GetOrCreateSearchAsync(cacheKey,
                () => registry.SearchAsync(q, skip, take, ct));

            return Results.Ok(results);
        })
        .WithName("SearchPackages")
        .WithSummary("Search for packages by name or keyword");

        // GET /api/packages/{registry}/{id}/{version}
        app.MapGet("/api/packages/{registryName}/{id}/{version}", async (
            string registryName,
            string id,
            string version,
            IPackageRegistry registry,
            ReflectionParser parser,
            CacheService cache,
            CancellationToken ct) =>
        {
            if (!ValidId.IsMatch(id))
                return Results.BadRequest(new { error = "Invalid package ID." });
            if (!ValidId.IsMatch(version))
                return Results.BadRequest(new { error = "Invalid version." });

            // Resolve "latest" para a versão mais recente real
            if (version.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                var versions = await registry.GetVersionsAsync(id, ct);
                if (versions.Length == 0)
                    return Results.NotFound(new { error = $"Package '{id}' not found." });
                version = versions[0]; // já vem ordenado desc, então [0] é o mais recente
            }
            try
            {
                var cacheKey = $"package:{registryName}:{id}:{version}";
                var result = await cache.GetOrCreateTypesAsync(cacheKey, async () =>
                {
                    var nupkgPath = await registry.DownloadPackageAsync(id, version, ct);
                    var metadata  = await registry.GetMetadataAsync(id, version, ct);
                    var namespaces = await parser.ExtractTypesAsync(nupkgPath, ct);
                    var hasSource = parser.HasEmbeddedSource(nupkgPath);

                    return new PackageDetail(
                        Id: id,
                        Version: version,
                        Metadata: new PackageMetadata(
                            LicenseUrl: metadata.LicenseUrl,
                            ProjectUrl: metadata.ProjectUrl,
                            Repository: metadata.RepositoryUrl is not null
                                ? new RepositoryInfo(metadata.RepositoryUrl, metadata.RepositoryType, metadata.CommitHash)
                                : default,
                            PublishedAt: metadata.PublishedAt
                        ),
                        TotalTypes: namespaces.Sum(ns => ns.Types.Count),
                        HasEmbeddedSource: hasSource,
                        Namespaces: namespaces
                    );
                });

                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, title: "Failed to process package", statusCode: 500);
            }
        })
        .WithName("GetPackage")
        .WithSummary("Analyze a package — download .nupkg and extract types");

        // GET /api/packages/{registry}/{id}/{version}/type/{typeName}
        app.MapGet("/api/packages/{registryName}/{id}/{version}/type/{typeName}", async (
            string registryName,
            string id,
            string version,
            string typeName,
            IPackageRegistry registry,
            ReflectionParser parser,
            CacheService cache,
            CancellationToken ct) =>
        {
            if (!ValidId.IsMatch(id) || !ValidId.IsMatch(version))
                return Results.BadRequest(new { error = "Invalid package ID or version." });

            try
            {
                var cacheKey = $"package:{registryName}:{id}:{version}";
                var pkg = await cache.GetOrCreateTypesAsync(cacheKey, async () =>
                {
                    var nupkgPath = await registry.DownloadPackageAsync(id, version, ct);
                    var metadata  = await registry.GetMetadataAsync(id, version, ct);
                    var namespaces = await parser.ExtractTypesAsync(nupkgPath, ct);
                    var hasSource = parser.HasEmbeddedSource(nupkgPath);

                    return new PackageDetail(
                        Id: id, Version: version,
                        Metadata: new PackageMetadata(metadata.LicenseUrl, metadata.ProjectUrl,
                            metadata.RepositoryUrl is not null
                                ? new RepositoryInfo(metadata.RepositoryUrl, metadata.RepositoryType, metadata.CommitHash)
                                : null,
                            PublishedAt: metadata.PublishedAt),
                        TotalTypes: namespaces.Sum(ns => ns.Types.Count),
                        HasEmbeddedSource: hasSource,
                        Namespaces: namespaces
                    );
                });

                var type = pkg.Namespaces
                    .SelectMany(ns => ns.Types)
                    .FirstOrDefault(t =>
                        t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                        t.FullName().Equals(typeName, StringComparison.OrdinalIgnoreCase));

                return type is null
                    ? Results.NotFound(new { error = $"Type '{typeName}' not found in {id} {version}." })
                    : Results.Ok(type);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        })
        .WithName("GetType")
        .WithSummary("Get a specific type with all its members");

        // GET /api/packages/{registry}/{id}/{version}/source/{*filePath}
        app.MapGet("/api/packages/{registryName}/{id}/{version}/source/{*filePath}", async (
            string registryName,
            string id,
            string version,
            string filePath,
            IPackageRegistry registry,
            ReflectionParser parser,
            CacheService cache,
            CancellationToken ct) =>
        {
            if (!ValidId.IsMatch(id) || !ValidId.IsMatch(version))
                return Results.BadRequest(new { error = "Invalid package ID or version." });

            try
            {
                var cacheKey = $"source:{registryName}:{id}:{version}:{filePath}";
                var source = await cache.GetOrCreateSourceAsync(cacheKey, async () =>
                {
                    var nupkgPath = await registry.DownloadPackageAsync(id, version, ct);
                    return parser.GetEmbeddedSource(nupkgPath, filePath);
                });

                return source is null
                    ? Results.NotFound(new { error = $"Source file '{filePath}' not found in package." })
                    : Results.Ok(new { filePath, source });
            }
            catch (System.Security.SecurityException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("GetSource")
        .WithSummary("Get an embedded .cs source file from the package");

        // GET /api/packages/{registry}/{id}/versions
        app.MapGet("/api/packages/{registryName}/{id}/versions", async (
            string registryName,
            string id,
            IPackageRegistry registry,
            CacheService cache,
            CancellationToken ct) =>
        {
            if (!ValidId.IsMatch(id))
                return Results.BadRequest(new { error = "Invalid package ID." });

            var cacheKey = $"versions:{registryName}:{id}";
            var versions = await cache.GetOrCreateMetadataAsync(cacheKey,
                () => registry.GetVersionsAsync(id, ct));

            return versions.Length == 0
                ? Results.NotFound(new { error = $"Package '{id}' not found." })
                : Results.Ok(new PackageVersionsResult(id, versions));
        })
        .WithName("GetVersions")
        .WithSummary("List available versions for a package");
    }
}

// Extension to compute FullName from TypeDetail
file static class TypeDetailExtensions
{
    public static string FullName(this GotoRef.Api.Domain.Models.TypeDetail t) => $"{t.Namespace}.{t.Name}";
}
