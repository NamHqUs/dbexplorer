using System.Security.Claims;
using DbExplorer.Application.Abstractions;
using DbExplorer.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DbExplorer.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/connections/{id:guid}/query")]
public sealed class QueryController(IQueryService query) : ControllerBase
{
    private string OwnerId => User.FindFirstValue("sub")!;

    [HttpPost]
    public async Task<ActionResult<QueryResponse>> Execute(Guid id, [FromBody] QueryRequest request, CancellationToken ct)
    {
        try
        {
            var result = await query.ExecuteAsync(OwnerId, id, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (QueryExecutionException ex)
        {
            return Problem(title: "SQL Server rejected the statement.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ConnectionReadOnlyException ex)
        {
            return Problem(title: ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
    }
}
