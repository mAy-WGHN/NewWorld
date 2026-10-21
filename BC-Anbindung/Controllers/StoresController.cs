using BC_Anbindung.BusinessCentral;
using BC_Anbindung.Common.Api;
using Microsoft.AspNetCore.Mvc;

namespace BC_Anbindung.Controllers;

[Route("api/stores")]
public class StoresController(BcGateway gateway) : ApiControllerBase
{
    /// <summary>Alle Filialen aus BC (Store-Nr. und Name), nach Store-Nr. sortiert. 503, wenn BC nicht erreichbar ist.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Success(await gateway.GetStoresAsync(ct));
}
