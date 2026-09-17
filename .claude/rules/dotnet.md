---
paths:
  - '**/*.cs'
  - '**/*.csproj'
  - '**/*.slnx'
  - 'src/**/*.razor'
---

# .NET 10 / C# 14 — stack-scoped rules

- **SDK**: .NET 10.0.112; `KnowledgeHub.slnx` is the solution file.
- **Build**: `dotnet build KnowledgeHub.slnx` — must stay 0 warnings.
- **Format gate**: `dotnet format KnowledgeHub.slnx --verify-no-changes` —
  CI fails on whitespace/style violations; run before committing.
- **Tests**: `dotnet test` — xUnit; unit tests in `tests/KnowledgeHub.Tests.Unit/`,
  integration (SQLite) in `tests/KnowledgeHub.Tests.Integration/`.
- **EF Core**: SQLite via EF Core migrations; `DatabaseMigrator` applies pending
  migrations at startup and baselines pre-migration databases. Every entity/
  `KnowledgeHubDbContext` change needs a committed migration.
- **Locked restore**: `packages.lock.json` per project
  (`RestorePackagesWithLockFile` in `Directory.Build.props`). After touching
  `PackageReference`, run `dotnet restore` and commit the locks.
- **APIs**: Minimal API groups under `src/KnowledgeHub.Server/Api/` —
  `MapGroup` + `Map{Verb}` per route; DTOs and contracts live in
  `KnowledgeHub.Shared/Contracts`.
- **MCP**: native engine in `KnowledgeHub.McpEngine/`; tools via
  `ToolProviders/` — keep `McpContractTests` tool-schema list in sync when
  adding/removing tools.
- **Client**: Blazor WebAssembly + BootstrapBlazor; keep layouts
  mobile-responsive (see `SPEC-20260916-mobile-layout-responsive`).
