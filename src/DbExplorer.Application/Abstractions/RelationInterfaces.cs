using DbExplorer.Application.Contracts;

namespace DbExplorer.Application.Abstractions;

public interface IRelationService
{
    // Returns null when the connection or start table does not exist or is not owned by the user.
    Task<RelationGraph?> BuildGraphAsync(string ownerUserId, Guid connectionId, RelationRequest request, CancellationToken ct);

    // Mode is "delete" (children first) or "insert" (parents first, with IDENTITY_INSERT).
    Task<CascadeScriptResponse?> GenerateScriptAsync(string ownerUserId, Guid connectionId, CascadeScriptRequest request, CancellationToken ct);
}
