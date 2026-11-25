# DbExplorer – Design Document (v1)

DbExplorer is a web tool for Dev and QA teams to manage database connections, browse schemas, view and edit table data in a grid, follow record relations, filter quickly, run raw SQL, and save queries.

## 1. Decisions

| Area | Decision |
|---|---|
| Database engine (v1) | SQL Server only. Provider abstraction is kept so other engines can be added later. |
| Backend | .NET 10 Web API, ADO.NET with raw SQL (no ORM for user data). |
| Frontend | React + Vite SPA, TypeScript. |
| SQL editor | Monaco Editor (syntax highlighting, SQL completion hooks). |
| Data grid | AG Grid Community (virtualized rows and columns, cell editing, custom filter popups). |
| Auth | Local accounts via ASP.NET Core Identity, JWT access token plus refresh token. |
| Storage of app data | SQLite via EF Core (`dbexplorer.db`). Holds Identity users, connections, saved queries, audit log. Target databases are never stored here. |
| Auth tokens | Access JWT (15 min) plus rotating refresh token (7 days). Refresh tokens are stored hashed in Identity's `AspNetUserTokens`. |
| Registration | First account can be created without config. Later accounts require `Auth:AllowRegistration = true`. |

## 2. Components

```
Browser (React + Vite SPA)
   │  HTTPS + JWT (REST/JSON)
   ▼
DbExplorer.Api (.NET 10 Web API)
   ├─ Auth module        (ASP.NET Core Identity, JWT)
   ├─ Connection module  (CRUD, encrypted connection strings, connection test)
   ├─ Metadata module    (schema download and cache: tables, columns, FKs, views, procs, functions)
   ├─ Data module        (paged table query, filtered query, insert/update/delete, lookup)
   ├─ Relation module    (record graph traversal, cascade script generation)
   ├─ Query module       (raw SQL execution, saved queries)
   └─ Target provider    (ADO.NET SqlConnection for SQL Server)
        │
        ├──► App DB  (users, connections, saved queries, audit)
        └──► Target DBs (the databases the user explores)
```

Layers inside the API: Controllers → Application services → Repositories/Providers. Controllers hold no SQL.

## 3. Data Model (app database)

- `AppUser` (Identity tables)
- `DbConnection`: Id, OwnerUserId, Name, DbType ('SqlServer'), Host, Port, Database, AuthMode (SqlAuth | WindowsAuth), UserName, EncryptedPassword, ReadOnly (bool), TrustServerCertificate (bool), TimeoutSec, CreatedAt, UpdatedAt
- `SavedQuery`: Id, OwnerUserId, ConnectionId (nullable), Name, Folder, SqlText, CreatedAt, UpdatedAt
- `AuditLog`: Id, UserId, ConnectionId, Action, Statement (truncated), RowsAffected, CreatedAt

Connection passwords are encrypted with ASP.NET Data Protection and never returned by the API.

## 4. Metadata Download and Cache

**Source queries (SQL Server):**
- Tables and columns: `sys.tables`, `sys.columns`, `sys.types`, `sys.identity_columns`, `sys.computed_columns`.
- Primary keys: `sys.key_constraints` joined to `sys.index_columns`.
- Foreign keys: `sys.foreign_keys` and `sys.foreign_key_columns` (both parent and referenced side).
- Views: `sys.views`. Procedures: `sys.procedures`. Functions: `sys.objects` with type `FN`, `IF`, `TF`.
- Parameters and definitions: `sys.parameters`, `sys.sql_modules`.

**Algorithm:**
1. Load each catalog view once per object kind, not once per table (avoids N+1 queries). Five queries cover objects with definitions, columns, primary keys, foreign keys, and parameters.
2. Build an in-memory model: `Table → Columns, PrimaryKey, ForeignKeys (outgoing), ReferencedBy (incoming)`. Routines carry their parameters and definition.
3. Cache the model per connection with a TTL (default 10 minutes). The cache key includes the connection's `UpdatedAt` ticks, so editing a connection invalidates its cached schema without an explicit call. `?refresh=true` forces a reload.

**Deferred:** per-kind lazy loading. Everything loads in one pass in v1.

**Concurrency:** two simultaneous first requests for the same connection may both load. This is accepted for v1 and is not locked.

## 5. Table Data View

**Request and response (rows)**
- `POST /api/connections/{id}/tables/{schema}/{table}/rows` with `{ startRow, endRow, sortModel: [{colId, sort}], filterModel: { colId: AG Grid filter }, quickFilter, includeCount }`.
- Blocks are capped at 1,000 rows. Response: `{ rows: [...], lastRow }`. `lastRow` is the total count when `includeCount` is true, otherwise null.

**Read path**
- The grid uses AG Grid's server-side row model: the client requests blocks by `startRow`/`endRow`.
- API builds `SELECT <cols> FROM [s].[t] <where> ORDER BY <sort>, <pk> OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY`. Sort columns come before the PK so paging stays stable.
- Without a PK and without a sort, ordering falls back to all orderable columns. If none exist, `ORDER BY (SELECT NULL)` is used. The response includes `hasPrimaryKey: false` so the UI can disable inline edits.
- Total count uses `SELECT COUNT_BIG(*) FROM ... <where>` and runs only when `includeCount` is true.

**Filter translation**
- Each AG Grid filter model (text, number, date, set) becomes a parameterized predicate. Column names come only from the cached metadata (whitelist), never from user text. Values are always bound as parameters.
- Text predicates compare `CAST([col] AS nvarchar(max))`, so they work on any column type. LIKE wildcards in user input are escaped.
- `quickFilter` (Ctrl+F) is an OR of contains-predicates across all non-binary columns. A `columns` list can narrow it to the active column.

**Write path (changes)**
- `POST .../changes` with `{ inserts: [{values}], updates: [{keys, values, rowVersion}], deletes: [{keys, rowVersion}] }`.
- Execution order inside one transaction: deletes, then updates, then inserts. Deletes run first so unique keys are freed before updates and inserts.
- Updates and deletes must match exactly one row. If a table has a `rowversion` column and the client sends `rowVersion`, it is added to the `WHERE` clause as an optimistic check.
- Inserts and updates use `OUTPUT inserted.*` so the grid receives generated identity values and the stored row.
- Any failing statement rolls back the whole batch. The response is `409` with per-operation errors `{group, index, message}`.
- Validation errors (unknown column, identity or computed column in values, missing PK keys) are `400` and no SQL is sent.
- Views cannot be written (`400`). Read-only connections are rejected with `403` before SQL is sent.
- Successful batches write one `AuditLog` row with the change counts.

## 6. Filter and Lookup UX

- **Ctrl+F** on a grid: opens a popup with a raw text filter. The text is matched against the active column (or all columns if none is selected). Implemented as server-side `LIKE` predicates.
- **Ctrl+L** on a cell: opens a lookup popup. It finds the referenced row through the foreign key that starts at this column. If the column is not a FK, it lists distinct values of that column with a count (top N, server-side `GROUP BY`).

## 7. Record Focus and Relation Graph

**Goal:** given selected rows in one table, show all related rows in other tables and generate cascade scripts.

**Algorithm (BFS over the FK graph):**
1. Start nodes: `(table, pkValues)` for each selected row.
2. For each node, follow outgoing FKs (parent rows) and incoming FKs (child rows), up to `MaxDepth` (default 3) and `MaxRows` per edge (default 500).
3. Each fetch is a parameterized `SELECT` on the related table with `WHERE fkColumns IN (...)`, batched per edge rather than per row. A visited set keyed by `(table, pk)` prevents cycles.
4. The result is a tree grouped by table, with counts and a "load more" action per edge.

**Cascade script generation:**
- Children must be deleted before parents. Build a dependency graph of the collected rows (child → parent) and topologically sort it.
- If the graph contains a cycle (self-referencing FK or mutual FKs), break it by emitting `UPDATE` to null the nullable FK column first. If the FK is not nullable, report the cycle and ask the user to choose.
- Output: `DELETE` statements in dependency order, or `INSERT` statements in reverse order for a copy-with-related-data script. Values are rendered as SQL literals with type-aware escaping (N'' for strings, hex for binary, ISO format for dates). Scripts are shown in Monaco and can be copied or saved.
- Scripts are generated only. Execution uses the normal raw-SQL path with a confirmation step.

**Limits:** a generated script is capped at a configurable number of statements (default 5,000). Above that, the UI shows a warning and offers to export to a file.

## 8. Table Search and Multi-table View

- Table search: client-side fuzzy search over the cached table list (name, schema). The server is not involved.
- Multiple tables: each table opens in its own tab. Tabs are independent grids. The workspace remembers open tabs per connection.

## 9. Raw SQL and Saved Queries

- Monaco editor with T-SQL language mode and keyword completion from the cached metadata (table and column names).
- Execution: `SqlCommand` with a timeout from the connection settings. Multiple result sets are supported and returned as pages. Display row cap defaults to 10,000.
- Read-only connections block statements that are not SELECT. The implemented guard (`ReadOnlyGuard`) is a tokenizer, not a T-SQL parser. Every statement must start with SELECT, WITH, or DECLARE, and no write, DDL, or EXEC keyword may appear outside comments, strings, and quoted identifiers. This guards against accidental writes; it is not a security boundary.
- Saved queries: CRUD per user, optionally scoped to a connection, organized in folders.

## 10. API Surface (v1)

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/auth/login`, `/api/auth/refresh` | Sign in, refresh JWT |
| GET/POST/PUT/DELETE | `/api/connections[/{id}]` | Manage connections |
| POST | `/api/connections/{id}/test` | Test connection |
| GET | `/api/connections/{id}/schema` | Cached model (`?refresh=true` to reload) |
| POST | `/api/connections/{id}/tables/{schema}/{table}/rows` | Server-side row block (filter, sort, paging) |
| POST | `/api/connections/{id}/tables/{schema}/{table}/changes` | Batch insert/update/delete |
| POST | `/api/connections/{id}/tables/{schema}/{table}/lookup` | Lookup popup data |
| POST | `/api/connections/{id}/relations` | Relation graph for selected rows |
| POST | `/api/connections/{id}/scripts/cascade` | Generate INSERT or DELETE scripts |
| POST | `/api/connections/{id}/query` | Execute raw SQL |
| GET/POST/PUT/DELETE | `/api/saved-queries[/{id}]` | Manage saved queries |

All responses are JSON. Errors use RFC 7807 problem details.

## 11. Security

- Passwords are encrypted with Data Protection and are never logged or returned.
- JWT is required for all `/api` routes except login and refresh. In v1, connections are visible only to their owner.
- All values are parameterized. Identifiers come from the cached whitelist and are bracket-quoted (`[name]`, with `]` escaped as `]]`).
- The audit log records statements executed through the write and raw SQL paths.
- Raw SQL is powerful by design for Dev/QA. Use a read-only connection for shared or production-like databases.

## 12. Performance Targets (initial, to validate)

- Open a table grid and show the first 200 rows: under 300 ms on a local SQL Server with a PK index.
- Schema load for 2,000 tables: under 3 s, one round trip per catalog view.
- Relation graph, depth 3, 500 rows per edge: under 2 s.
- Grid scrolling stays smooth through AG Grid row virtualization; no full table is loaded into the browser.

Measured on the local SQL Server 2022 container with 50,000 rows (`PERF` probe, not kept in the repo):
- Schema load (one table): 250 ms. First 200-row page with count: 56 ms. Filtered page: 51 ms. Page at offset 40,000: 12 ms.

## 13. Project Layout (current)

```
dbexplorer/
  DbExplorer.sln
  docs/DESIGN.md
  docs/progress/phase-1.md
  src/DbExplorer.Api/            (.NET 10 Web API: controllers, auth, Program.cs)
  src/DbExplorer.Application/    (domain entities, contracts, service interfaces)
  src/DbExplorer.Infrastructure/ (EF Core SQLite app DB, Identity, connection service, SQL Server tester)
  tests/DbExplorer.Api.Tests/    (xUnit integration tests via WebApplicationFactory)
  web/                           (React + Vite + TypeScript SPA: AG Grid, Monaco, relations, saved queries)
```

## 14. Delivery Phases

Status: all seven phases and the SPA are implemented. Progress and test results are in `docs/progress/`.

1. Backend skeleton, auth, connection CRUD, connection test.
2. Schema download and cache.
3. Table grid: paged read, filter translation, batch edit.
4. Ctrl+F filter and Ctrl+L lookup.
5. Relation graph and cascade script generation.
6. Raw SQL with Monaco, saved queries, audit log.
7. Hardening: performance checks, read-only guard, tests.

## 15. Open Items

Decided with defaults for v1 (confirm or change):
- Views, procedures, and functions: browse source only. Procedure execution with parameter input is deferred.
- Schema list: all schemas are listed. Schema filtering is deferred.
- Export: CSV for grid data, SQL for generated scripts. Excel is deferred.
