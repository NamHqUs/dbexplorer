using System.Security.Claims;
using DbExplorer.Application.Abstractions;
using DbExplorer.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DbExplorer.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/saved-queries")]
public sealed class SavedQueriesController(ISavedQueryService savedQueries) : ControllerBase
{
    private string OwnerId => User.FindFirstValue("sub")!;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavedQueryDto>>> List([FromQuery] Guid? connectionId, CancellationToken ct) =>
        Ok(await savedQueries.ListAsync(OwnerId, connectionId, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SavedQueryDto>> Get(Guid id, CancellationToken ct)
    {
        var dto = await savedQueries.GetAsync(OwnerId, id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<SavedQueryDto>> Create([FromBody] SavedQueryRequest request, CancellationToken ct)
    {
        var dto = await savedQueries.CreateAsync(OwnerId, request, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SavedQueryDto>> Update(Guid id, [FromBody] SavedQueryRequest request, CancellationToken ct)
    {
        var dto = await savedQueries.UpdateAsync(OwnerId, id, request, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await savedQueries.DeleteAsync(OwnerId, id, ct) ? NoContent() : NotFound();
}
