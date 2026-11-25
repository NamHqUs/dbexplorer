namespace DbExplorer.Application.Contracts;

public sealed record QueryRequest(string Sql, int? TimeoutSec, int? MaxRows);

public sealed record QueryResponse(IReadOnlyList<ResultSet> ResultSets, int RowsAffected, long DurationMs);

// Rows are positional and follow Columns. Positional rows keep duplicate column names (e.g. two "Id" columns) intact.
public sealed record ResultSet(IReadOnlyList<ColumnInfo> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, bool Truncated);

public sealed record ColumnInfo(string Name, string SqlType);

public sealed record SavedQueryRequest(string Name, string? Folder, string SqlText, Guid? ConnectionId);

public sealed record SavedQueryDto(
    Guid Id,
    string Name,
    string? Folder,
    string SqlText,
    Guid? ConnectionId,
    DateTimeOffset UpdatedAt);
