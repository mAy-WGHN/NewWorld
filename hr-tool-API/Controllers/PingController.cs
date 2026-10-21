using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrTool_API.Controllers;

/// <summary>Erreichbarkeits- und Zeitabgleich ohne fachliche Last, ohne Anmeldung aufrufbar.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", time = DateTimeOffset.UtcNow });
}
