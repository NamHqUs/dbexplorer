using System.Security.Claims;
using DbExplorer.Application.Abstractions;
using DbExplorer.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DbExplorer.Api.Controllers;

[ApiController]
[Authorize]
public sealed class RelationsController(IRelationService relations) : ControllerBase
{
    private string OwnerId => User.FindFirstValue("sub")!;

    [HttpPost("api/connections/{id:guid}/relations")]
    public async Task<ActionResult<RelationGraph>> Graph(Guid id, [FromBody] RelationRequest request, CancellationToken ct)
    {
        var graph = await relations.BuildGraphAsync(OwnerId, id, request, ct);
        return graph is null ? NotFound() : Ok(graph);
    }

    [HttpPost("api/connections/{id:guid}/scripts/cascade")]
    public async Task<ActionResult<CascadeScriptResponse>> Script(Guid id, [FromBody] CascadeScriptRequest request, CancellationToken ct)
    {
        var script = await relations.GenerateScriptAsync(OwnerId, id, request, ct);
        return script is null ? NotFound() : Ok(script);
    }
}
