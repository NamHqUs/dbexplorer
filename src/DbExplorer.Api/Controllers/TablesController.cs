using System.Security.Claims;
using DbExplorer.Application.Abstractions;
using DbExplorer.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DbExplorer.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/connections/{id:guid}/tables/{schema}/{table}")]
public sealed class TablesController(ITableDataService tableData) : ControllerBase
{
    private string OwnerId => User.FindFirstValue("sub")!;

    [HttpPost("rows")]
    public async Task<ActionResult<TableRowsResponse>> GetRows(
        Guid id, string schema, string table, [FromBody] TableRowsRequest request, CancellationToken ct)
    {
        var result = await tableData.GetRowsAsync(OwnerId, id, schema, table, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("lookup")]
    public async Task<ActionResult<LookupResponse>> Lookup(
        Guid id, string schema, string table, [FromBody] LookupRequest request, CancellationToken ct)
    {
        var result = await tableData.LookupAsync(OwnerId, id, schema, table, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("changes")]
    public async Task<ActionResult<ChangeBatchResult>> ApplyChanges(
        Guid id, string schema, string table, [FromBody] ChangeBatchRequest request, CancellationToken ct)
    {
        try
        {
            var result = await tableData.ApplyChangesAsync(OwnerId, id, schema, table, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ChangeBatchException ex)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The change batch was rolled back.",
                Detail = ex.Message,
            };
            problem.Extensions["errors"] = ex.Errors;
            return Conflict(problem);
        }
        catch (ConnectionReadOnlyException ex)
        {
            return Problem(title: ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
    }
}
