using System.Security.Claims;
using DbExplorer.Application.Abstractions;
using DbExplorer.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DbExplorer.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/connections")]
public sealed class ConnectionsController(IConnectionService connections, ISqlServerConnectionTester tester) : ControllerBase
{
    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConnectionDto>>> List(CancellationToken ct) =>
        Ok(await connections.ListAsync(OwnerId, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConnectionDto>> Get(Guid id, CancellationToken ct)
    {
        var dto = await connections.GetAsync(OwnerId, id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<ConnectionDto>> Create([FromBody] CreateConnectionRequest request, CancellationToken ct)
    {
        var dto = await connections.CreateAsync(OwnerId, request, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ConnectionDto>> Update(Guid id, [FromBody] UpdateConnectionRequest request, CancellationToken ct)
    {
        var dto = await connections.UpdateAsync(OwnerId, id, request, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await connections.DeleteAsync(OwnerId, id, ct) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<TestConnectionResult>> Test(Guid id, CancellationToken ct)
    {
        var found = await connections.GetWithSecretAsync(OwnerId, id, ct);
        if (found is null) return NotFound();

        var result = await tester.TestAsync(found.Value.Connection, found.Value.Password, ct);
        return Ok(result);
    }
}
