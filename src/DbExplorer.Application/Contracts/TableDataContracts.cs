using System.Text.Json;

namespace DbExplorer.Application.Contracts;

public sealed record TableRowsRequest(
    int StartRow,
    int EndRow,
    IReadOnlyList<SortItem>? SortModel,
    IReadOnlyDictionary<string, ColumnFilter>? FilterModel,
    string? QuickFilter,
    IReadOnlyList<string>? QuickFilterColumns,
    bool IncludeCount = true);

public sealed record SortItem(string ColId, string Sort);

// Mirrors AG Grid filter models. Numbers and dates arrive as JSON numbers or strings, so values are kept as JsonElement.
public sealed record ColumnFilter(
    string FilterType,
    string? Type,
    JsonElement? Filter,
    JsonElement? FilterTo,
    IReadOnlyList<JsonElement>? Values);

public sealed record TableRowsResponse(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    long? LastRow,
    bool HasPrimaryKey);

public sealed record ChangeBatchRequest(
    IReadOnlyList<InsertChange>? Inserts,
    IReadOnlyList<UpdateChange>? Updates,
    IReadOnlyList<DeleteChange>? Deletes);

public sealed record InsertChange(IReadOnlyDictionary<string, JsonElement>? Values);

public sealed record UpdateChange(
    IReadOnlyDictionary<string, JsonElement> Keys,
    IReadOnlyDictionary<string, JsonElement>? Values,
    string? RowVersion);

public sealed record DeleteChange(IReadOnlyDictionary<string, JsonElement> Keys, string? RowVersion);

public sealed record ChangeBatchResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Inserted,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Updated,
    int Deleted);

public sealed record ChangeError(string Group, int Index, string Message);

public sealed record LookupRequest(string Column, JsonElement? Value, string? Search, int Top = 50);

// Mode is "foreignKey" (referenced rows) or "distinct" (values with counts).
public sealed record LookupResponse(
    string Mode,
    string? TargetSchema,
    string? TargetTable,
    string? TargetColumn,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Rows,
    IReadOnlyList<LookupValue>? Values);

public sealed record LookupValue(object? Value, long Count);
