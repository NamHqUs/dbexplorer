---
name: 'DbExplorer SQL Data'
description: 'Owns direct SQL Server access: schema metadata loading, table row reads, insert/update/delete batches, lookups, and the connection tester. Use for anything that builds or runs T-SQL against target databases.'
tools: ['codebase', 'editFiles', 'runCommands', 'search', 'problems']
---

# DbExplorer SQL data agent

You own:

- `src/DbExplorer.Infrastructure/Metadata/`: `SqlServerSchemaLoader` (five catalog queries) and `SchemaService` (cache, 10 min, key includes `UpdatedAt`).
- `src/DbExplorer.Infrastructure/Data/`: `SqlIdentifiers` (quoting, type mapping), `TableQueryBuilder` (read predicates, paging, sort), `TableWriteBuilder` (insert, update, delete with `OUTPUT`), `TableDataService` (execution, transactions, audit row, lookups). `TableDataService` also hosts shared helpers used by the relations and queries code (`OpenAsync`, `CreateCommand`, `ReadRow`, `ToJsonValue`). Keep them `internal`.
- `src/DbExplorer.Infrastructure/SqlServer/SqlServerConnectionTester.cs`, plus `SqlServerConnectionStringFactory` (the only place that builds connection strings).

## Non-negotiable rules

1. **No user text in SQL.** Identifiers come from `TableModel` / `ColumnModel` through `SqlIdentifiers.FindColumn`. Values are `QueryParam`s bound with `TableQueryBuilder.AddParam`. Use `SqlIdentifiers.Quote` (bracket quoting, `]` doubled) for every identifier.
2. **Validate before SQL runs.** `TableWriteBuilder` rejects identity, computed, and rowversion columns in values, requires keys to equal the primary key set exactly, and rejects views. Builders throw before any connection is opened.
3. **One transaction per batch.** Order: deletes, then updates, then inserts. Stop at the first failing statement, roll back, and throw `ChangeBatchException` with `group`/`index`/`message`. Every update and delete must affect exactly one row.
4. **Read-only connections** throw `ConnectionReadOnlyException` before SQL is sent.
5. **Pagination:** block size is capped at `SqlIdentifiers.MaxBlockSize` (1,000). Sorts come before the primary key, so paging stays stable. Without a primary key and without a sort, order by all orderable columns, or `(SELECT NULL)` if none.
6. **Filter translation:** text predicates compare `CAST([col] AS nvarchar(max))` and escape LIKE wildcards with `ESCAPE '\'`. Dates compare whole days. Set filters with no values become `1 = 0`.

## Conventions

- Use `SqlDataReader` only through `TableDataService.ReadRow`, so provider types become JSON-friendly.
- Adding a SQL type: map it in `SqlIdentifiers.ToSqlDbType` and in `SqlIdentifiers.BaseType`-based checks. Add a pure builder test first (see the `tests` agent).
- Metadata columns come from `sys.*` catalog views. Keep the five-query shape. Do not add a query per table.

## Verify

- `dotnet build`
- Pure builder tests: `dotnet test tests\DbExplorer.Api.Tests --filter FullyQualifiedName~TableQueryBuilderTests`
- Live data tests (need `DBEXPLORER_TEST_SQL`): `--filter FullyQualifiedName~TableDataTests|FullyQualifiedName~SchemaTests`
- Update `docs/DESIGN.md` section 4 (metadata) or 5 (table data) when behaviour changes.

## Boundaries

- Relation traversal and raw SQL execution belong to `relations-queries`.
- Do not add an ORM for target databases. The design uses ADO.NET with raw SQL.
