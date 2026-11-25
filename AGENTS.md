# DbExplorer agents

DbExplorer is a Dev/QA tool: a .NET 10 Web API (`src/`), a React + Vite SPA (`web/`), and xUnit integration tests (`tests/`). Each component has a custom agent in `.github/agents/`. Pick the agent whose scope matches the files you are changing. For a change that crosses components, start with the agent for the layer closest to the request and follow the handoffs below.

| Agent file | Scope | Main paths |
|---|---|---|
| `api.agent.md` | HTTP layer: controllers, auth endpoints, middleware, `Program.cs` | `src/DbExplorer.Api/` |
| `application.agent.md` | Contracts, domain entities, service interfaces | `src/DbExplorer.Application/` |
| `persistence-identity.agent.md` | EF Core app database, migrations, Identity, secret protection, DI wiring | `src/DbExplorer.Infrastructure/Persistence/`, `Security/`, `DependencyInjection.cs` |
| `sql-data.agent.md` | Target SQL Server access: table rows, writes, lookups, metadata, connection tester | `src/DbExplorer.Infrastructure/Data/`, `Metadata/`, `SqlServer/` |
| `relations-queries.agent.md` | FK graph walk, cascade scripts, raw SQL, read-only guard, saved queries | `src/DbExplorer.Infrastructure/Relations/`, `Queries/` |
| `tests.agent.md` | Integration and pure unit tests, live SQL fixtures | `tests/DbExplorer.Api.Tests/` |
| `web.agent.md` | React SPA: API client, components, AG Grid, Monaco | `web/` |
| `docs.agent.md` | Design doc and phase progress notes | `docs/` |

## Shared rules

- Design first: update `docs/DESIGN.md` before changing behaviour, then write code.
- Layering: Controllers → Application interfaces → Infrastructure implementations. Controllers hold no SQL and no EF queries.
- Never build SQL from user text. Identifiers (schema, table, column) must come from the cached schema model: columns through `SqlIdentifiers.FindColumn`, tables through the schema lookup in `TableDataService`. Values are always bound as parameters.
- Every query on user data is scoped to the owner (`OwnerUserId`). A foreign owner gets `404`, not `403`.
- Errors: `ArgumentException` → 400 (middleware in `Program.cs`), `ChangeBatchException` → 409, `ConnectionReadOnlyException` → 403, `QueryExecutionException` → 400.
- Comments only where the code needs clarification.
- Commit trailer when committing: `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`.

## Verification commands

```powershell
dotnet build                                   # whole solution
dotnet test tests\DbExplorer.Api.Tests         # set DBEXPLORER_TEST_SQL for live SQL tests
dotnet ef migrations add <Name> --project src\DbExplorer.Infrastructure --startup-project src\DbExplorer.Api --output-dir Persistence\Migrations
cd web; npm run build                          # typecheck + Vite build
```

## Handoffs

- New endpoint: `api` (route + status mapping) → `application` (contract + interface) → the implementing agent (`sql-data`, `relations-queries`, or `persistence-identity`) → `tests` → `docs`.
- New column type or SQL rule: `sql-data` → `tests` (pure builder test first).
- New UI feature: `web` → `api` if it needs a new endpoint → `docs`.
