using HrTool_API.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrTool_API.Tests.Infrastructure;

/// <summary>
/// Nur für die Tests (per ApplicationPart eingebunden): Solange das HR-Tool keine eigenen geschützten Routen hat, braucht die Rechteprüfung
/// (Token, Rollen-Rechte, Scope) eine Route, an der sie sich prüfen lässt. Das Recht <c>probe:read</c> steht bewusst nicht im Katalog der Anwendung.
/// </summary>
[ApiController]
[Route("api/probe")]
public class ProbeController(ScopeContext scope) : ControllerBase
{
    public const string Privilege = "probe:read";

    [HttpGet]
    [HasPrivilege(Privilege)]
    public IActionResult Get() => Ok(new { all = scope.Current.All, locationIds = scope.Current.Locations });

    [HttpGet("open")]
    [AllowAnonymous]
    public IActionResult Open() => Ok(new { open = true });
}
