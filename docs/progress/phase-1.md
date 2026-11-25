# Phase 1 – Backend skeleton, auth, connection CRUD, connection test

Status: complete for the backend. The React SPA is not started.

## What was built

- Solution `DbExplorer.sln` with projects `DbExplorer.Api`, `DbExplorer.Application`, `DbExplorer.Infrastructure`, `DbExplorer.Api.Tests`. Target framework: .NET 10.
- Auth: ASP.NET Core Identity (local accounts), JWT access tokens (15 min), rotating refresh tokens (7 days, stored hashed).
- Registration: the first account is open. Later accounts need `Auth:AllowRegistration = true`.
- Connections: CRUD scoped to the owner. Passwords are encrypted with Data Protection and never returned (`hasPassword` only).
- Connection test: opens a SQL Server connection and runs `SELECT @@VERSION`. Failures return `200` with `success: false` and the error message.
- Migrations: `InitialCreate` in `src/DbExplorer.Infrastructure/Persistence/Migrations`. Applied at startup.

## Endpoints

| Method | Route | Auth |
|---|---|---|
| POST | `/api/auth/register` | none (see registration rule) |
| POST | `/api/auth/login` | none |
| POST | `/api/auth/refresh` | none (refresh token in body) |
| GET, POST | `/api/connections` | JWT |
| GET, PUT, DELETE | `/api/connections/{id}` | JWT |
| POST | `/api/connections/{id}/test` | JWT |

## Test results

Command: `dotnet test tests\DbExplorer.Api.Tests`
Result: 6 passed, 0 failed.

| Test | Verifies |
|---|---|
| Registration_is_open_only_for_the_first_account | Bootstrap rule; second registration gets 403 when registration is closed |
| Refresh_token_is_rotated_and_the_old_one_is_rejected | Old refresh token gets 401 after rotation |
| Connections_require_authentication | Unauthenticated request gets 401 |
| Created_connection_never_returns_the_password_and_is_hidden_from_other_users | No password in response; other users get 404 |
| SQL_auth_connection_without_password_is_rejected | Validation returns 400 |
| Test_connection_reports_failure_for_unreachable_server_without_throwing | Failed test returns `success: false`, not 500 |

Not covered yet: a live SQL Server connection test. No SQL Server instance was available in this environment.

## Developer notes

- Package versions are pinned to the 10.x line. NuGet's latest is 11.x, which does not target net10.0.
- `dotnet-ef` 10.x is installed globally. Regenerate migrations with:
  `dotnet ef migrations add <Name> --project src\DbExplorer.Infrastructure --startup-project src\DbExplorer.Api --output-dir Persistence\Migrations`
- Development without `Jwt:SigningKey` uses a random key. Tokens are invalidated on restart. Outside Development, startup fails without a key of at least 32 bytes.
- The app database is `dbexplorer.db` in the API working directory. It is git-ignored.

## Next

Phase 2: schema download and cache (catalog queries, per-connection cache with TTL, refresh endpoint).
