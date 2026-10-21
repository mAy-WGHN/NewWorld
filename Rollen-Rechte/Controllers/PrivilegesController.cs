using Rollen_Rechte.Authentication;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Contracts;
using Rollen_Rechte.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Rollen_Rechte.Controllers;

/// <summary>Der Rechte-Katalog (<see cref="PrivilegeSnapshot"/>: generierte plus eigene Rechte): Daraus baut die UI die Rollen-Matrix.</summary>
[Route("api/privileges")]
public class PrivilegesController(PrivilegeCatalogService catalog) : ApiControllerBase
{
    [HttpGet, HasPrivilege<AclRole>(CrudAction.Read)]
    public async Task<IActionResult> Catalog(CancellationToken ct) =>
        Success((await catalog.GetAsync(ct)).Areas
            .Select(a => new PrivilegeAreaResponse(a.Key, a.Label,
                a.Actions.Select(x => new PrivilegeActionResponse(x.Privilege, x.Action, x.Label, x.Custom, x.Scopable)).ToList()))
            .ToList());
}
