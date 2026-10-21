using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BC_Anbindung.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/ping")]
public class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Ping() => Ok(new { success = true, data = "pong" });
}
