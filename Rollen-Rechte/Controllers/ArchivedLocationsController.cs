using Rollen_Rechte.Domain.Entities;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Contracts;
using Rollen_Rechte.Data;
using Rollen_Rechte.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Rollen_Rechte.Controllers;

[Route("api/locations/archived")]
public class ArchivedLocationsController(AccessDbContext db, ScopeContext scope) : ApiControllerBase
{
    [HttpGet, HasPrivilege<Location>(CrudAction.Read)]
    public async Task<IActionResult> List([FromQuery] LocationFilterRequest filter, CancellationToken ct) =>
        Success(await LocationListing.ListAsync(db, filter, archived: true, scope.Current, ct));
}
