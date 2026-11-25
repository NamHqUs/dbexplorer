using DbExplorer.Application.Contracts;
using DbExplorer.Application.Domain;

namespace DbExplorer.Application.Abstractions;

public interface IConnectionService
{
    Task<IReadOnlyList<ConnectionDto>> ListAsync(string ownerUserId, CancellationToken ct);
    Task<ConnectionDto?> GetAsync(string ownerUserId, Guid id, CancellationToken ct);
    Task<ConnectionDto> CreateAsync(string ownerUserId, CreateConnectionRequest request, CancellationToken ct);
    Task<ConnectionDto?> UpdateAsync(string ownerUserId, Guid id, UpdateConnectionRequest request, CancellationToken ct);
    Task<bool> DeleteAsync(string ownerUserId, Guid id, CancellationToken ct);

    // Returns the connection with its decrypted password, for internal use only. Never expose via API.
    Task<(DbConnection Connection, string? Password)?> GetWithSecretAsync(string ownerUserId, Guid id, CancellationToken ct);
}

public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string cipherText);
}

public interface ISqlServerConnectionTester
{
    Task<TestConnectionResult> TestAsync(DbConnection connection, string? password, CancellationToken ct);
}
