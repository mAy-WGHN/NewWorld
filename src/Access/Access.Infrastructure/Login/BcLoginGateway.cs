using System.Net.Http.Json;
using Access.Core.Login;

namespace Access.Infrastructure.Login;

/// <summary>BC Integration client (the only way to BC): asks it to check personnel number and PIN. Needs the app role <c>BcIntegration.Pin.Verify</c>.</summary>
public sealed class BcLoginGateway(HttpClient http) : IBcLoginGateway
{
    public async Task<BcPinResult> VerifyAsync(string personnelNumber, string pin, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("/internal/employees/verify-pin", new Request(personnelNumber, pin), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new BcLoginUnavailableException(); // 503: BC unreachable; anything else is a configuration problem. Never forward details.
            }

            var answer = await response.Content.ReadFromJsonAsync<Response>(cancellationToken) ?? throw new BcLoginUnavailableException();
            return new BcPinResult(answer.Authenticated);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException
                                       || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new BcLoginUnavailableException();
        }
    }

    private sealed record Request(string PersonnelNumber, string Pin);

    private sealed record Response(bool Authenticated, string? EmployeeName);
}
