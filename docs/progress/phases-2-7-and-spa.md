# Phases 2–7 and SPA – Progress and test results

Status: all phases implemented. The backend and the React SPA were verified in tests and in the browser.

## What was built

| Phase | Result |
|---|---|
| 2. Schema | `GET /api/connections/{id}/schema[?refresh=true]` loads tables, columns, PKs, FKs (both directions), views, procedures, functions, and parameters in five catalog queries. Cached per connection for 10 min. The cache key includes the connection's `UpdatedAt`. |
| 3. Table data | `POST .../tables/{schema}/{table}/rows` (server-side paging, AG Grid filter translation, sort, Ctrl+F quick filter). `POST .../changes` (batched insert/update/delete in one transaction, rowversion optimistic check, `OUTPUT` rows, `AuditLog` row). |
| 4. Lookup | `POST .../tables/{schema}/{table}/lookup`: FK column → referenced rows; other column → distinct values with counts. |
| 5. Relations | `POST /api/connections/{id}/relations` (BFS over the FK graph, one batched query per edge per level, depth and row limits). `POST /api/connections/{id}/scripts/cascade` (DELETE children first, INSERT parents first with IDENTITY_INSERT, nullable-FK cycle breaking). |
| 6. Raw SQL | `POST /api/connections/{id}/query` (multiple result sets, row cap 10,000, timeout, read-only guard, audit row). Saved queries CRUD at `/api/saved-queries`. |
| 7. Hardening | Read-only guard tests, owner-isolation tests, rollback tests, build warnings fixed, performance probe measured (numbers in DESIGN.md §12). |
| SPA | `web/`: login, connections (create/edit/test/delete), schema tree with search, multi-tab table grids (AG Grid infinite model), Ctrl+F find, Ctrl+L lookup popup, relation panel with DELETE/INSERT scripts, Monaco SQL editor with result sets and saved queries. |

## Test results

Command: `dotnet test tests\DbExplorer.Api.Tests` with `DBEXPLORER_TEST_SQL` set.
Result: **43 passed, 0 failed, 0 skipped**. Run twice with the same result.

| Area | Count | Live SQL needed |
|---|---|---|
| Auth, connections, owner isolation (ApiTests) | 6 | No |
| Read-only guard (theory cases) | 10 | No |
| Query builder and write builder (pure) | 6 | No |
| Cascade script builder (pure) | 5 | No |
| Schema | 3 | Yes |
| Table data: rows, batch, rollback, conflicts, read-only, views, lookup | 8 | Yes |
| Relation graph and DELETE script order | 1 | Yes |
| Query execution, saved queries | 4 | Partly (query tests need SQL) |

Frontend: `npm run build` (typecheck plus Vite build) passes.

Browser check (integrated browser, live API and SQL Server container):
- Sign in, select connection, schema tree loads.
- `dbo.Customers` grid shows rows with the correct `RowVer` values.
- Ctrl+L on `dbo.Orders.CustomerId` opens the lookup and lists the referenced `Customers` row.
- Relations for one customer show the customer and its two orders at depth 1. The DELETE script deletes both orders before the customer.
- The toolbar Find button filters the grid. "No Rows To Show" with `0 row(s) · filtered by "zzz"`.
- SQL tab runs `SELECT ...; SELECT ...` and shows two result sets with the correct values.

## How to run the live tests

The live tests use a SQL Server container. Start it with a password you choose at run time (do not commit it):

```powershell
docker run -d --name dbexplorer-test -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=<strong password>" -p 14330:1433 mcr.microsoft.com/mssql/server:2022-latest
$env:DBEXPLORER_TEST_SQL = "Server=127.0.0.1,14330;User Id=sa;Password=<same password>;TrustServerCertificate=True"
dotnet test tests\DbExplorer.Api.Tests
```

Without `DBEXPLORER_TEST_SQL`, the live tests are skipped (SkippableFact).

Run the SPA with `npm install` and `npm run dev` inside `web/`. The API must run on `http://localhost:5105` (see `Properties/launchSettings.json`). Vite proxies `/api` to it.

## Developer notes

- Live-database test classes share one collection (`LiveSqlCollection`). They reset the same rows, so they must not run in parallel.
- `SqlTestDatabase` recreates `DbExplorerTests` once per test run. The tests create their own users and connections, so they do not depend on each other's data.
- Monaco is bundled through `loader.config({ monaco })` in `web/src/main.tsx`, so the editor works offline.
- AG Grid's header checkbox is not supported by the infinite row model, so it is turned off.
- Keyboard handling uses capture-phase listeners for Ctrl+F and Ctrl+Enter. AG Grid and Monaco otherwise stop those events.
- The SPA keeps the session in `sessionStorage` (per tab, cleared when the tab closes). It is not in `localStorage`.

## Known limitations

- **Ctrl+F in the integrated browser:** the browser's own find shortcut captured the key in testing, so the shortcut could not be verified there. The toolbar Find button runs the same action and was verified. In a normal browser the page's handler receives the shortcut.
- **Insert scripts with self-references:** a table whose FK points to itself produces a warning and its rows are left out. Delete scripts break such cycles with `UPDATE ... SET fk = NULL` when the FK is nullable.
- **Relation values are JSON-friendly:** provider types such as `geography` or `hierarchyid` are read as text. The INSERT script then writes them as string literals, which SQL Server will reject for those types.
- **Query results:** the server returns up to 10,000 rows per result set. The SPA displays the first 1,000 rows of each set.
- **Schema cache:** two first requests for the same connection can both load the schema. The cost is one extra catalog load, not incorrect data.
- **Read-only guard:** a tokenizer, not a parser. It stops accidental writes. It is not a security boundary.
- **Bundle size:** the SPA bundle is about 1.5 MB minified, mostly Monaco and AG Grid. Lazy-loading the editor is a possible follow-up.
- **Registration:** the first account can be created without configuration. Later accounts need `Auth:AllowRegistration`.
