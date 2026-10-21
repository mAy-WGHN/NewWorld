using Microsoft.Extensions.Options;

namespace BC_Anbindung.BusinessCentral;

public class BcCodeunitClient(IHttpClientFactory httpClientFactory, IBcAuthService authService, IOptions<BcAuthOptions> options)
    : IBcCodeunitClient
{
    public const string HttpClientName = "BusinessCentral";

    private readonly BcAuthOptions _options = options.Value;

    public async Task<string> CallAsync(
        string codeunitName, string operationName, IEnumerable<(string Name, string Value)> parameters, CancellationToken ct)
    {
        var parametersXml = string.Concat(parameters.Select(p =>
            $"<{p.Name}>{System.Security.SecurityElement.Escape(p.Value)}</{p.Name}>"));
        var ns = $"urn:microsoft-dynamics-schemas/codeunit/{codeunitName}";
        var soapUrl = $"https://api.businesscentral.dynamics.com/v2.0/{_options.TenantId}/{_options.Environment}" +
                      $"/WS/{_options.CompanyName}/Codeunit/{codeunitName}";
        var envelope = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <{operationName} xmlns="{ns}">{parametersXml}</{operationName}>
              </soap:Body>
            </soap:Envelope>
            """;

        var token = await authService.GetAccessTokenAsync(ct);
        var client = httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, soapUrl)
        {
            Content = new StringContent(envelope, System.Text.Encoding.UTF8, "text/xml")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("SOAPAction", $"\"{ns}:{operationName}\"");

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        // Der Body kann die Parameter (u. a. das PIN) enthalten — er wird deshalb nur in Details mitgegeben,
        // nie in die Meldung, und die Aufrufer loggen Details nicht.
        if (!response.IsSuccessStatusCode)
            throw new BcAuthException($"BC-Aufruf '{codeunitName}.{operationName}' fehlgeschlagen ({(int)response.StatusCode}).", null);

        return LscCodeunitResponse.ExtractReturnValue(body);
    }
}
