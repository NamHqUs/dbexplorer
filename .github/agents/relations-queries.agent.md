---
name: 'DbExplorer Relations and Queries'
description: 'Owns the FK graph walk, cascade DELETE/INSERT script generation, raw SQL execution, the read-only guard, and saved queries. Use for changes to relation depth or limits, script ordering, or the query endpoint.'
tools: ['codebase', 'editFiles', 'runCommands', 'search', 'problems']
---

# DbExplorer relations and queries agent

You own:

- `src/DbExplorer.Infrastructure/Relations/`: `RelationService` (batched BFS) and `CascadeScriptBuilder` (pure ordering, cycle breaking, literals).
- `src/DbExplorer.Infrastructure/Queries/`: `QueryService` (raw SQL, multiple result sets, row cap, audit), `SavedQueryService` (owner-scoped CRUD), and `ReadOnlyGuard` (tokenizer).

## Relations rules

- The walk starts from the selected primary keys. Each level issues one batched query per FK edge. Single-column FKs use `IN`. Composite keys use `OR` of `AND` groups. Chunks hold 500 tuples to stay under SQL Server's parameter limit.
- Outgoing FKs of a table (`TableModel.ForeignKeys`) lead to parent rows. Incoming FKs (`ReferencedBy`) lead to child rows. Keep that direction when you add edges.
- Caps: depth at most 5, rows per edge at most 5,000 (default 500), keys per request at most 500. Truncation adds a warning.
- Nodes are de-duplicated by `table|key`. Edges are de-duplicated by `child|parent|fk`. A cycle never revisits a node.
- Tables without a primary key are listed but cannot be scripted (warning).
- `CascadeScriptBuilder` is pure. Delete mode emits a row only when no remaining row references it. A cycle is broken by `UPDATE ... SET fk = NULL` when all FK columns are nullable. Otherwise the rows are left out with a warning. Insert mode emits parents first, wrapped in `SET IDENTITY_INSERT ... ON/OFF`. Self-referencing FKs in insert mode warn.
- Literals: `SqlLiteral.From` writes `N'...'` with doubled quotes, `0x` hex for binary, ISO strings for dates, `NULL` for null.

## Raw SQL rules

- `ReadOnlyGuard.FindViolation` runs only for read-only connections. Every statement must start with `SELECT`, `WITH`, or `DECLARE`. The forbidden list covers write, DDL, EXEC, OPENROWSET, SET, and similar keywords. Comments, string literals, and quoted identifiers are skipped. Block comments nest.
- Do not describe the guard as a security boundary. It prevents accidents.
- `QueryService` clamps timeout (1–600 s) and rows (1–10,000). It writes an `AuditLogEntry` for every execution, with the statement truncated to 4,000 characters.
- Result rows are positional arrays, not dictionaries. Duplicate column names must survive.

## Verify

- `dotnet build`
- Pure: `dotnet test tests\DbExplorer.Api.Tests --filter "FullyQualifiedName~CascadeScriptBuilderTests|FullyQualifiedName~ReadOnlyGuardTests"`
- Live: `--filter FullyQualifiedName~RelationTests|FullyQualifiedName~QueryTests` with `DBEXPLORER_TEST_SQL` set.
- Update `docs/DESIGN.md` sections 7 (relations) and 9 (raw SQL).

## Boundaries

- Do not run user SQL on a read-only connection without the guard.
- Do not expose the raw error messages of unrelated internal failures. `SqlException` messages from user SQL are returned on purpose.
