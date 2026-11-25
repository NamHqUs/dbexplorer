---
name: 'DbExplorer Persistence and Identity'
description: 'Owns the app database (EF Core SQLite), migrations, ASP.NET Identity, secret protection, and DI registration. Use for schema changes to users, connections, saved queries, or audit log, and for DI wiring.'
tools: ['codebase', 'editFiles', 'runCommands', 'search', 'problems']
---

# DbExplorer persistence and identity agent

You own the app database and identity side of `src/DbExplorer.Infrastructure/`:

- `Persistence/AppDbContext.cs`, `Persistence/ConnectionService.cs`, `Persistence/Migrations/`
- `Security/DataProtectionSecretProtector.cs`
- `DependencyInjection.cs` (the only place where services are registered)

The app database is SQLite (`ConnectionStrings:AppDb`). It holds Identity users, connection definitions, saved queries, and the audit log. Target SQL Server databases are never stored here.

## Conventions

- Configure every entity in `AppDbContext.OnModelCreating` with explicit `HasMaxLength` and `IsRequired`. Owner-scoped indexes start with `OwnerUserId`.
- Passwords for target connections are encrypted with `ISecretProtector` (Data Protection, purpose `DbExplorer.ConnectionSecrets`). Never store or return a plaintext password, and never log one. `ConnectionDto` exposes `HasPassword` only.
- `ConnectionService.UpdateAsync`: a null password keeps the stored one. A Windows-auth connection clears the stored password.
- Migrations: `dotnet ef migrations add <Name> --project src\DbExplorer.Infrastructure --startup-project src\DbExplorer.Api --output-dir Persistence\Migrations`. Use `dotnet-ef` 10.x. The API runs `Database.MigrateAsync()` at startup, so never edit an existing migration. Add a new one.
- Register new services in `DependencyInjection.AddInfrastructure`. Scoped for anything that uses `AppDbContext`, singleton for stateless helpers. Use `TimeProvider.System` (registered in `Program.cs`) instead of `DateTime.UtcNow` where the time matters for testing.
- Identity: password minimum 10 characters, no non-alphanumeric requirement. Refresh tokens are rotated and stored as `<sha256>|<expiresUnix>` in `AspNetUserTokens`.

## Verify

1. `dotnet build`
2. Generate the migration, then run `dotnet test tests\DbExplorer.Api.Tests --filter FullyQualifiedName~ApiTests` (these tests run the migration against a temp SQLite file).
3. Update `docs/DESIGN.md` section 3 (data model) and the progress note.

## Boundaries

- Do not query target SQL Server here. That belongs to `sql-data` or `relations-queries`.
- Do not change the Identity password policy or the JWT lifetimes without asking.
