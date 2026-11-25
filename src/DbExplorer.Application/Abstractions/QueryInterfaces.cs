using DbExplorer.Application.Contracts;

namespace DbExplorer.Application.Abstractions;

public interface IQueryService
{
    // Returns null when the connection does not exist or is not owned by the user.
    // Throws ConnectionReadOnlyException when a read-only connection receives a non-SELECT statement,
    // and QueryExecutionException when SQL Server rejects the statement.
    Task<QueryResponse?> ExecuteAsync(string ownerUserId, Guid connectionId, QueryRequest request, CancellationToken ct);
}

public interface ISavedQueryService
{
    Task<IReadOnlyList<SavedQueryDto>> ListAsync(string ownerUserId, Guid? connectionId, CancellationToken ct);
    Task<SavedQueryDto?> GetAsync(string ownerUserId, Guid id, CancellationToken ct);
    Task<SavedQueryDto> CreateAsync(string ownerUserId, SavedQueryRequest request, CancellationToken ct);
    Task<SavedQueryDto?> UpdateAsync(string ownerUserId, Guid id, SavedQueryRequest request, CancellationToken ct);
    Task<bool> DeleteAsync(string ownerUserId, Guid id, CancellationToken ct);
}

public sealed class QueryExecutionException(string message) : Exception(message);
