using Portal.Domain.Entities;
using Portal.Authentication;
using Portal.Common.Api;
using Portal.Contracts;
using Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Portal.Controllers;

[Route("api/reports/archived")]
public class ArchivedReportsController(ReportQueryService queries) : ApiControllerBase
{
    // Wie die normale Liste, aber archived wird immer auf true erzwungen.
    [HttpGet, HasPrivilege<Report>(CrudAction.Read)]
    public async Task<IActionResult> List([FromQuery] ReportFilterRequest filter, CancellationToken ct) =>
        Success(await queries.ListAsync(filter, forceArchived: true, ct));
}
