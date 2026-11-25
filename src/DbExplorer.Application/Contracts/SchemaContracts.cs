namespace DbExplorer.Application.Contracts;

public sealed record DatabaseSchema(
    DateTimeOffset LoadedAt,
    IReadOnlyList<TableModel> Tables,
    IReadOnlyList<RoutineModel> Procedures,
    IReadOnlyList<RoutineModel> Functions);

// Tables and views share this shape. IsView distinguishes them.
public sealed record TableModel(
    string Schema,
    string Name,
    bool IsView,
    string? Definition,
    IReadOnlyList<ColumnModel> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<ForeignKeyModel> ForeignKeys,
    IReadOnlyList<ForeignKeyModel> ReferencedBy);

public sealed record ColumnModel(
    string Name,
    int Ordinal,
    string SqlType,
    bool IsNullable,
    bool IsIdentity,
    bool IsComputed,
    bool IsRowVersion);

public sealed record ForeignKeyModel(
    string Name,
    string ParentSchema,
    string ParentTable,
    IReadOnlyList<string> ParentColumns,
    string ReferencedSchema,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns,
    string OnDelete);

public sealed record RoutineModel(
    string Schema,
    string Name,
    string Kind,
    string? Definition,
    IReadOnlyList<ParameterModel> Parameters);

public sealed record ParameterModel(string Name, string SqlType, bool IsOutput);
