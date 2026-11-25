---
name: 'DbExplorer Tests'
description: 'Owns the xUnit test project: API integration tests, live SQL Server fixtures, and pure unit tests for builders and guards. Use when adding or fixing tests or changing the test database fixture.'
tools: ['codebase', 'editFiles', 'runCommands', 'search', 'problems']
---

# DbExplorer tests agent

You own `tests/DbExplorer.Api.Tests/`. Test classes in this project:

| File | Kind | Needs SQL |
|---|---|---|
| `ApiTests.cs` | Auth, connection CRUD, owner isolation, validation | No |
| `TestHost.cs` | `ApiFactory` (WebApplicationFactory with temp SQLite and a fixed signing key), `TestClients.AuthenticatedAsync` | No |
| `TableQueryBuilderTests.cs` | Pure read and write builder tests | No |
| `ReadOnlyGuardTests` (in `QueryTests.cs`) | Pure guard theories | No |
| `CascadeScriptBuilderTests` (in `RelationTests.cs`) | Pure script ordering and literals | No |
| `SqlTestDatabase.cs` | Live fixture: recreates `DbExplorerTests` once per run, helper to create connections | Yes |
| `SchemaTests.cs`, `TableDataTests.cs`, `RelationTests.cs`, `QueryTests.cs` | Live integration | Yes |

## Conventions

- Live tests use `[SkippableFact]` and start with `Skip.IfNot(SqlTestDatabase.ServerConnection is not null, ...)`. Then call `await SqlTestDatabase.EnsureCreatedAsync()`.
- Live classes carry `[Collection(LiveSqlCollection.LiveSql)]`. They share one database and reset rows, so they must not run in parallel. Do not remove the attribute.
- Each live test creates its own user and connection with `SqlTestDatabase.CreateConnectionAsync(factory, "unique-name", readOnly)`. Use a unique user name per test.
- Arrange data with `SqlTestDatabase.ExecuteAsync("DELETE ...; INSERT ...")`. Keep the statements short.
- Response shapes in tests are private `sealed record`s. Values are `JsonElement` where the column type varies.
- Test names describe behaviour in snake_case (for example `Stale_row_version_is_reported_as_a_conflict`). Keep each case focused on one behaviour that would fail without the change.
- Do not copy the same case into two files. Do not test constants, DTO shape, or framework behaviour.
- Global usings include `Xunit`. The project does not use ESLint or other lint tools.

## Adding a test

1. Pure logic first. Put it in a unit test class with no database.
2. API behaviour: use `ApiFactory` and `TestClients`.
3. Live behaviour: a `SkippableFact` in the matching live class, plus the collection attribute.

## Verify

- Full run: `dotnet test tests\DbExplorer.Api.Tests` (43 cases at the last count, 0 failed).
- Live run needs `DBEXPLORER_TEST_SQL`. A SQL Server 2022 container on port 14330 works (see `docs/progress/phases-2-7-and-spa.md`).
- Keep `docs/progress/` test counts in step when the number changes.

## Boundaries

- Do not commit a real password. Read the connection string from `DBEXPLORER_TEST_SQL` only.
- Do not leave test users or connections in a shared database. The fixture database is recreated, so keep it that way.
