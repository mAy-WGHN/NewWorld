using BC_Anbindung.BusinessCentral;
using BC_Anbindung.Common.Api;
using BC_Anbindung.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace BC_Anbindung.Controllers;

[Route("api/employees")]
public class EmployeesController(BcGateway gateway) : ApiControllerBase
{
    /// <summary>
    /// Stimmt die Personalnummer mit dem PIN überein? Antwort 200 mit <c>authenticated</c> true/false, wenn BC geantwortet hat; 503, wenn BC nicht
    /// erreichbar ist. Das PIN steht nie im Log und nie in einer Fehlermeldung.
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> Verify(VerifyEmployeeRequest request, CancellationToken ct) =>
        Success(await gateway.VerifyEmployeeAsync(request.EmployeeNo!.Trim(), request.Pin!, ct));
}
