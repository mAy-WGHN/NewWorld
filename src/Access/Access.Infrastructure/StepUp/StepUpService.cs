using System.Security.Cryptography;
using System.Text;
using Access.Core.StepUp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Access.Infrastructure.StepUp;

public enum StartStatus
{
    Started = 0,
    Unavailable = 1,
    NoLink = 2,
}

public sealed record StartResult(StartStatus Status, string? AuthorizationUrl = null);

/// <param name="Succeeded">Whether the confirmation was stored.</param>
/// <param name="Code">Internal reason (logged, never sent to the browser); <c>Ok</c> on success.</param>
public sealed record CompleteResult(bool Succeeded, string Code);

/// <summary>
/// The step-up with Microsoft Entra (see <see cref="StepUpOptions"/>): starts a sign-in, completes it at the callback and tells whether a
/// person has a valid confirmation. Nothing about the sign-in is kept besides the confirmation and the binding of the Entra object id.
/// Logging: failure codes only, never tokens, codes, names or addresses.
/// </summary>
public sealed partial class StepUpService(
    AccessDbContext db,
    EntraOidcClient entra,
    EntraIdTokenValidator validator,
    StepUpOptions options,
    TimeProvider time,
    ILogger<StepUpService> logger)
{
    /// <summary>How long a started step-up can be completed (state, nonce, verifier).</summary>
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(10);

    public async Task<StartResult> StartAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return new StartResult(StartStatus.Unavailable);
        }

        if (!await db.EntraLinks.AnyAsync(l => l.EmployeeId == employeeId, cancellationToken))
        {
            return new StartResult(StartStatus.NoLink);
        }

        var now = time.GetUtcNow();
        await db.StepUpRequests.Where(r => r.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);

        var state = RandomToken();
        var nonce = RandomToken();
        var verifier = RandomToken();
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string url;
        try
        {
            url = await entra.BuildAuthorizationUrlAsync(state, nonce, challenge, cancellationToken);
        }
        catch (EntraUnavailableException)
        {
            LogFailed("ProviderUnavailable");
            return new StartResult(StartStatus.Unavailable);
        }

        db.StepUpRequests.Add(new StepUpRequestRecord
        {
            StateHash = Hash(state),
            EmployeeId = employeeId,
            Nonce = nonce,
            CodeVerifier = verifier,
            ExpiresAt = now + RequestLifetime,
        });
        await db.SaveChangesAsync(cancellationToken);
        return new StartResult(StartStatus.Started, url);
    }

    /// <summary>Handles the return of the browser from Entra: state once, code exchange, token checks, assignment to the person, confirmation.</summary>
    public async Task<CompleteResult> CompleteAsync(string? code, string? state, bool providerReportedError, CancellationToken cancellationToken)
    {
        var result = await TryCompleteAsync(code, state, providerReportedError, cancellationToken);
        if (!result.Succeeded)
        {
            LogFailed(result.Code);
        }

        return result;
    }

    private async Task<CompleteResult> TryCompleteAsync(string? code, string? state, bool providerReportedError, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(state))
        {
            return Fail("StateMissing");
        }

        // State is redeemed once: whoever deletes the row owns the request, a second use (or a parallel one) finds nothing.
        var hash = Hash(state);
        var request = await db.StepUpRequests.AsNoTracking().FirstOrDefaultAsync(r => r.StateHash == hash, cancellationToken);
        if (request is null || await db.StepUpRequests.Where(r => r.StateHash == hash).ExecuteDeleteAsync(cancellationToken) != 1)
        {
            return Fail("StateUnknown");
        }

        var now = time.GetUtcNow();
        if (request.ExpiresAt <= now)
        {
            return Fail("StateExpired");
        }

        if (providerReportedError || string.IsNullOrEmpty(code))
        {
            return Fail("ProviderError");
        }

        IdTokenResult token;
        try
        {
            var idToken = await entra.ExchangeCodeAsync(code, request.CodeVerifier, cancellationToken);
            if (idToken is null)
            {
                return Fail("CodeRejected");
            }

            token = await validator.ValidateAsync(idToken, request.Nonce, cancellationToken);
        }
        catch (EntraUnavailableException)
        {
            return Fail("ProviderUnavailable");
        }

        if (token.Identity is not { } identity)
        {
            return Fail("Token" + token.Failure);
        }

        var link = await db.EntraLinks.FirstOrDefaultAsync(l => l.EmployeeId == request.EmployeeId, cancellationToken);
        if (link is null)
        {
            return Fail("NoLink");
        }

        if (link.EntraObjectId is { } bound)
        {
            if (bound != identity.ObjectId)
            {
                return Fail("IdentityMismatch");
            }
        }
        else if (!string.Equals(link.UserPrincipalName, identity.UserPrincipalName, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("UpnMismatch");
        }
        else
        {
            link.EntraObjectId = identity.ObjectId;
            link.BoundAt = now;
        }

        var confirmation = await db.StepUpConfirmations.FirstOrDefaultAsync(c => c.EmployeeId == request.EmployeeId, cancellationToken);
        if (confirmation is null)
        {
            confirmation = new StepUpConfirmationRecord { EmployeeId = request.EmployeeId };
            db.StepUpConfirmations.Add(confirmation);
        }

        confirmation.ConfirmedAt = now;
        confirmation.ValidUntil = now + options.Validity;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Fail("IdentityInUse"); // the same Entra account is already bound to another person
        }

        return new CompleteResult(true, "Ok");

        static CompleteResult Fail(string reason) => new(false, reason);
    }

    /// <summary>The end of the person's confirmation, or <c>null</c> when there is none that still counts.</summary>
    public async Task<DateTimeOffset?> ConfirmedUntilAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return await db.StepUpConfirmations.AsNoTracking()
            .Where(c => c.EmployeeId == employeeId && c.ValidUntil > now)
            .Select(c => (DateTimeOffset?)c.ValidUntil)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string RandomToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Step-up failed: {Code}")]
    private partial void LogFailed(string code);
}
