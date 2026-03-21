# gotoref-api

Backend da aplicação [gotoref.dev](https://gotoref.dev) — explore tipos, membros e XML docs de pacotes NuGet.

## Stack

- .NET 8 · ASP.NET Core Minimal API
- `NuGet.Protocol` para busca e download de pacotes
- `System.Reflection.Metadata` + `PEReader` para introspecção segura (sem `Assembly.Load`)
- `IMemoryCache` para cache em duas camadas (L1 in-process, L2 Redis — futuro)
- Rate limiting por IP (30 req/min)

## Endpoints

| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/health` | Health check |
| GET | `/api/packages/search?q={query}&take={n}` | Busca pacotes |
| GET | `/api/packages/{registry}/{id}/versions` | Lista versões |
| GET | `/api/packages/{registry}/{id}/{version}` | Tipos completos do pacote |
| GET | `/api/packages/{registry}/{id}/{version}/type/{typeName}` | Tipo específico |
| GET | `/api/packages/{registry}/{id}/{version}/source/{*filePath}` | Arquivo .cs embarcado |

## Exemplos de URL

```
GET /api/packages/search?q=newtonsoft&take=10
GET /api/packages/nuget/Newtonsoft.Json/versions
GET /api/packages/nuget/Newtonsoft.Json/13.0.3
GET /api/packages/nuget/Newtonsoft.Json/13.0.3/type/JsonConvert
GET /api/packages/nuget/Newtonsoft.Json/13.0.3/source/JsonConvert.cs
```

## Rodando localmente

```bash
cd src
dotnet run
```

Swagger disponível em `http://localhost:5000/swagger`.

## Variáveis de ambiente

| Variável | Descrição |
|----------|-----------|
| `ASPNETCORE_ENVIRONMENT` | `Development` ou `Production` |
| `GITHUB_TOKEN` | Aumenta rate limit da GitHub API (futuro) |
| `REDIS_CONNECTION` | String de conexão Redis (L2 cache — futuro) |

## Segurança implementada

- `PEReader` only — nunca `Assembly.Load`
- Rate limiting: 30 req/min por IP
- Limite de 100MB por `.nupkg`
- Path traversal guard no ZIP
- Sanitização de IDs de pacote (`^[a-zA-Z0-9._-]+$`)
- Security headers: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`
- CORS restrito aos domínios configurados
- Timeout de 30s em todas as chamadas externas

## Roadmap de Registries

```
v1 → NuGet  + System.Reflection.Metadata  ✅
v2 → npm    + TypeScript Compiler API
v3 → PyPI   + Python AST
v4 → Maven  + JavaParser
v5 → Crates + syn (Rust)
```

## Licença

MIT
