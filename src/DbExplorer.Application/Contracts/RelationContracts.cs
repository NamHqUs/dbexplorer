using System.Text.Json;

namespace DbExplorer.Application.Contracts;

// Keys hold the primary key values of the selected rows, keyed by column name.
public sealed record RelationRequest(
    string Schema,
    string Table,
    IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Keys,
    int MaxDepth = 3,
    int MaxRowsPerEdge = 500);

public sealed record RelationGraph(
    IReadOnlyList<RelationNode> Nodes,
    IReadOnlyList<RelationEdge> Edges,
    IReadOnlyList<string> Warnings);

public sealed record RelationNode(
    string Id,
    string Schema,
    string Table,
    int Depth,
    IReadOnlyDictionary<string, object?> Row);

// Child references parent through ForeignKey, so the child row must be deleted before the parent row.
public sealed record RelationEdge(string ChildId, string ParentId, string ForeignKey);

public sealed record CascadeScriptRequest(RelationRequest Relation, string Mode = "delete");

public sealed record CascadeScriptResponse(
    IReadOnlyList<string> Statements,
    IReadOnlyList<string> Warnings,
    bool Truncated);
