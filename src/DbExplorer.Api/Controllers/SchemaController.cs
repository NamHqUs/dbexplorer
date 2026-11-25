using DbExplorer.Application.Abstractions;
using DbExplorer.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DbExplorer.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/connections/{id:guid}/schema")]
public sealed class SchemaController(ISchemaService schema) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DatabaseSchema>> Get(Guid id, [FromQuery] bool refresh, CancellationToken ct)
    {
        var owner = User.FindFirst("sub")!.Value;
        try
        {
            var result = await schema.GetAsync(owner, id, refresh, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            return Problem(title: "Could not load schema from the database.", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
