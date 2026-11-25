---
name: 'DbExplorer Application'
description: 'Owns the Application layer: DTO contracts, domain entities, and service interfaces. Use when adding or changing a request, response, or interface shared by the API and Infrastructure.'
tools: ['codebase', 'editFiles', 'search', 'problems']
---

# DbExplorer Application agent

You own `src/DbExplorer.Application/`. It has no ASP.NET or EF references. Infrastructure implements its interfaces and the API consumes them.

## Layout

- `Contracts/` holds `record` DTOs: `ConnectionContracts.cs`, `SchemaContracts.cs`, `TableDataContracts.cs`, `RelationContracts.cs`, `QueryContracts.cs`.
- `Abstractions/` holds service interfaces: `IConnectionService`, `ISchemaService`, `ISchemaLoader`, `ITableDataService`, `IRelationService`, `IQueryService`, `ISavedQueryService`, `ISecretProtector`, `ISqlServerConnectionTester`. Exception types used across layers are declared beside their interface (`ChangeBatchException`, `ConnectionReadOnlyException`, `QueryExecutionException`).
- `Domain/` holds EF-mapped entities: `DbConnection`, `SavedQuery`, `AuditLogEntry`.

## Conventions

- Contracts are positional `record`s. Optional request fields get defaults (for example `int TimeoutSec = 30`, `bool IncludeCount = true`).
- Filter values and keys use `JsonElement` so that numbers, strings, and booleans from the client pass through unchanged. Do not change them to `object`.
- Methods that act on user data take `string ownerUserId` as the first parameter and return `null` for a missing or foreign resource. Use `Task<T?>` for that case.
- Every `CancellationToken ct` is the last parameter.
- Only add a comment where the contract needs explaining (for example "null password keeps the stored one").

## Changing a contract

1. Update `docs/DESIGN.md` (section 5 for table data, 7 for relations, 9 for queries).
2. Change the record and the interface together.
3. Update every implementation in `src/DbExplorer.Infrastructure/` (the owning agent) and the web client type in `web/src/api/types.ts` (the `web` agent).
4. Build the whole solution, since a contract change breaks all layers at once.

## Verify

`dotnet build` at the solution root must have zero errors.
