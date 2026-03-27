<p align="center">
  <h1 align="center">GotoRef API</h1>
  <p align="center">
    <strong>Explore NuGet packages like never before.</strong><br/>
    Browse types, members, docs and cross-package references — all from a single API.
  </p>
  <p align="center">
    <a href="https://gotoref.dev"><img alt="Live" src="https://img.shields.io/badge/live-gotoref.dev-blue?style=flat-square"/></a>
    <a href="https://github.com/AugustoDeveloper/gotoref-api/actions"><img alt="CI" src="https://img.shields.io/github/actions/workflow/status/AugustoDeveloper/gotoref-api/deploy.yml?branch=main&style=flat-square&label=CI"/></a>
    <a href="https://github.com/AugustoDeveloper/gotoref-api/releases"><img alt="Version" src="https://img.shields.io/github/v/tag/AugustoDeveloper/gotoref-api?label=version&style=flat-square"/></a>
    <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-MIT-green?style=flat-square"/></a>
  </p>
</p>

---

## What is GotoRef?

GotoRef lets you **introspect any NuGet package** without installing it. Search packages, list versions, extract every public type with its properties, methods, fields, events, XML documentation — and navigate across package dependencies with full type-reference resolution.

**Built for developers who want to understand a package before adding it.**

## Features

- **Safe reflection** — uses `PEReader` + `System.Reflection.Metadata`, never `Assembly.Load`
- **Cross-package navigation** — type references carry package origin metadata (`TypeRef`), enabling click-through from one package to another
- **XML docs included** — summaries, parameter docs and return descriptions extracted from embedded documentation
- **Embedded source** — browse `.cs` files shipped inside symbol packages
- **Fast caching** — tiered in-memory cache (search: 10min, metadata: 24h, types: 7d, source: 30d)
- **Secure by default** — rate limiting, input validation, path traversal protection, security headers

## Quick Start

```bash
# Clone & run
git clone https://github.com/AugustoDeveloper/gotoref-api.git
cd gotoref-api/src/api
dotnet run
```

Open **http://localhost:8001/swagger** to explore the API.

## API Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/health` | Health check |
| `GET` | `/api/packages/search?q={query}&take={n}` | Search packages |
| `GET` | `/api/packages/{registry}/{id}/versions` | List versions |
| `GET` | `/api/packages/{registry}/{id}/{version}` | Full type extraction |
| `GET` | `/api/packages/{registry}/{id}/{version}/type/{typeName}` | Single type detail |
| `GET` | `/api/packages/{registry}/{id}/{version}/source/{*filePath}` | Embedded source file |

### Try it

```bash
# Search for a package
curl "http://localhost:8001/api/packages/search?q=newtonsoft&take=5"

# Get all types from Newtonsoft.Json
curl "http://localhost:8001/api/packages/nuget/Newtonsoft.Json/13.0.3"

# Inspect a specific type
curl "http://localhost:8001/api/packages/nuget/Newtonsoft.Json/13.0.3/type/JsonConvert"
```

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 8 · ASP.NET Core Minimal API |
| Package introspection | `System.Reflection.Metadata` + `PEReader` |
| NuGet integration | `NuGet.Protocol` |
| Caching | `IMemoryCache` with tiered TTLs |
| Security | Rate limiting (30 req/min), CORS, input validation |
| Deploy | Docker + Railway |
| CI/CD | GitHub Actions · Conventional Commits · Semver |

## Type Reference Resolution

Every type in the API response includes a `TypeRef` field with package origin metadata:

```jsonc
{
  "displayName": "ILogger<JsonReader>",
  "packageId": "Microsoft.Extensions.Logging.Abstractions",
  "packageVersion": "8.0.0",
  "isCurrentPackage": false,
  "typeArguments": [
    {
      "displayName": "JsonReader",
      "packageId": null,
      "isCurrentPackage": true
    }
  ]
}
```

This enables the frontend to create **navigation links between packages** — click a type reference to jump directly to that type in its originating package.

## Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `Cors:AllowedOrigins` | `http://localhost:3000` | Comma-separated allowed CORS origins |
| `ASPNETCORE_ENVIRONMENT` | `Development` | Controls logging and Swagger |

## Roadmap

| Version | Registry | Parser |
|---------|----------|--------|
| **v1** | NuGet | `System.Reflection.Metadata` |
| v2 | npm | TypeScript Compiler API |
| v3 | PyPI | Python AST |
| v4 | Maven | JavaParser |
| v5 | Crates | `syn` (Rust) |

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

1. Fork the repository
2. Create your feature branch (`git checkout -b feat/amazing-feature`)
3. Commit using [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, etc.)
4. Push and open a Pull Request

## License

[MIT](LICENSE)
