# gotoref-api
 
Backend da aplicação [gotoref.dev](https://gotoref.dev) — explora tipos, membros e XML docs de pacotes NuGet.
 
## Stack
 
- .NET 8 · ASP.NET Minimal API
- `NuGet.Protocol` para busca e download de pacotes
- `System.Reflection.Metadata` para introspecção das DLLs sem carregar assemblies
 
## Endpoints
 
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/health` | Health check |
| GET | `/search?q={termo}&skip=0&take=10` | Busca pacotes no NuGet.org |
| GET | `/package/{id}/versions` | Lista versões disponíveis |
| GET | `/package/{id}/{version}` | Retorna tipos, membros e XML docs |
 
## Rodando localmente
 
```bash
cd src
dotnet run
```
 
Swagger disponível em `http://localhost:5000/swagger`.
 
## Deploy
 
O projeto está configurado para deploy no [Railway](https://railway.app) via Dockerfile.
 
## Licença
 
MIT
 
