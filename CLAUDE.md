# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Run locally (from src/api/)
dotnet run

# Build for production
dotnet publish -c Release -f net8.0 -r linux-x64

# Build Docker image (from src/api/)
docker build -t gotoref-api .
```

There are no automated tests in this project yet.

## Architecture

**GotoRef API** is an ASP.NET Core Minimal API (.NET 8) that allows clients to introspect NuGet packages — searching packages, listing versions, and extracting type/member information from assemblies without loading them into the runtime.

### Request flow

```
HTTP Request → PackageEndpoints → CacheService → IPackageRegistry → ILanguageParser → Response
```

1. **PackageEndpoints** (`endpoints/`) validates input and checks cache before delegating
2. **CacheService** (`domain/services/`) wraps `IMemoryCache` with tiered TTLs (search: 10min, metadata: 24h, types: 7d, source: 30d)
3. **NuGetRegistry** (`registries/`) searches NuGet.org and downloads `.nupkg` files (100MB limit, 30s timeout)
4. **ReflectionParser** (`parsers/`) extracts types using `PEReader` + `System.Reflection.Metadata` — deliberately avoids `Assembly.Load` to stay safe

### Type reference resolution

Every type reference in the API response (base types, interfaces, property/field/event types, method return types, parameter types) includes a `TypeRef` companion field (`*Ref`) with package origin metadata:

- **`RichTypeDecoder`** (`parsers/`) — `ISignatureTypeProvider<TypeRef, object?>` that resolves each type's `ResolutionScope` to determine if it belongs to the current package, an external NuGet package, or the BCL
- **`AssemblyPackageMap`** (`parsers/`) — parses the `.nuspec` from the `.nupkg` to map assembly names to `(packageId, version)` via direct name matching against declared dependencies
- **`TypeRef`** record (`domain/models/`) — carries `DisplayName`, `PackageId?`, `PackageVersion?`, `IsCurrentPackage`, and `TypeArguments?` (each generic arg is itself a navigable `TypeRef`)

The old `string` fields (`BaseType`, `TypeName`, `ReturnType`, etc.) remain unchanged for backward compatibility. The new `*Ref` fields are additive and nullable with defaults.

### Extensibility

Both `IPackageRegistry` and `ILanguageParser` are interfaces designed for future expansion (npm, PyPI, etc.). New registries/parsers are registered in `Program.cs` via DI. `ILanguageParser.ExtractTypesAsync` receives `packageId` to enable type reference resolution.

### Key configuration (environment variables)

| Variable | Default | Purpose |
|---|---|---|
| `Cors:AllowedOrigins` | `http://localhost:3000` | Comma-separated allowed CORS origins |
| `ASPNETCORE_ENVIRONMENT` | `Development` | Controls logging level |

Local dev runs on **http://localhost:8001**. Swagger UI is available at `/swagger` in Development mode.

### Security constraints (do not weaken)

- Package ID/version inputs are validated against `^[a-zA-Z0-9._-]+$`
- File paths extracted from `.nupkg` are normalized to prevent path traversal
- Reflection uses `PEReader` only — never `Assembly.Load` or `Assembly.LoadFrom`
- Rate limit: 30 req/min per IP (fixed window, configured in `Program.cs`)
