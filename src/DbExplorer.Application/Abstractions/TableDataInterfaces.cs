using DbExplorer.Application.Contracts;

namespace DbExplorer.Application.Abstractions;

public interface ITableDataService
{
    // Returns null when the connection or table does not exist or is not owned by the user.
    Task<TableRowsResponse?> GetRowsAsync(string ownerUserId, Guid connectionId, string schema, string table, TableRowsRequest request, CancellationToken ct);

    // Returns null when the connection or table does not exist or is not owned by the user.
    // Throws ChangeBatchException on SQL failure, ConnectionReadOnlyException for read-only connections.
    Task<ChangeBatchResult?> ApplyChangesAsync(string ownerUserId, Guid connectionId, string schema, string table, ChangeBatchRequest request, CancellationToken ct);

    // Ctrl+L. Returns null when the connection or table does not exist or is not owned by the user.
    Task<LookupResponse?> LookupAsync(string ownerUserId, Guid connectionId, string schema, string table, LookupRequest request, CancellationToken ct);
}

public sealed class ChangeBatchException(IReadOnlyList<ChangeError> errors)
    : Exception("One or more changes failed. The batch was rolled back.")
{
    public IReadOnlyList<ChangeError> Errors { get; } = errors;
}

public sealed class ConnectionReadOnlyException(string message = "This connection is read-only. Changes are not allowed.")
    : Exception(message);
