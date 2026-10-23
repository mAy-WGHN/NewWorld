using BcIntegration.Core.Login;

namespace BcIntegration.Infrastructure.BusinessCentral;

public sealed class BcPinVerifier(BcClient client) : IBcPinVerifier
{
    public async Task<PinCheck> VerifyAsync(string personnelNumber, string pin, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(personnelNumber) || string.IsNullOrWhiteSpace(pin))
        {
            return new PinCheck(false, null);
        }

        var answer = await client.VerifyPinAsync(personnelNumber, pin, cancellationToken);
        var authenticated = answer.Authenticated && !string.IsNullOrWhiteSpace(answer.EmployeeName);
        return new PinCheck(authenticated, authenticated ? answer.EmployeeName : null);
    }
}
