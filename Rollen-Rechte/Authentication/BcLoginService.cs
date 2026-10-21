using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rollen_Rechte.Data;
using Rollen_Rechte.Domain.Entities;
using Rollen_Rechte.Services;

namespace Rollen_Rechte.Authentication;

public enum BcLoginOutcome
{
    /// <summary>BC wurde nicht gefragt (nicht konfiguriert oder Username ist keine Personalnummer): lokale Prüfung.</summary>
    NotApplicable,

    /// <summary>BC ist technisch nicht erreichbar: lokale Prüfung gegen den zuletzt gespeicherten Stand.</summary>
    Unavailable,

    /// <summary>BC kennt Personalnummer/PIN nicht (oder es darf kein User angelegt werden).</summary>
    Rejected,

    Authenticated
}

public record BcLoginResult(BcLoginOutcome Outcome, User? User = null);

/// <summary>
/// Anmeldung per BC-Personalnummer + PIN über den Dienst "BC-Anbindung" (<see cref="IBcGateway"/>): BC vergleicht das PIN selbst und
/// antwortet nur mit Erfolg/Misserfolg und dem Namen.
///
/// - Ein LEERES PIN wird nie an BC gegeben (BC würde es als gültig werten); das prüft auch die BC-Anbindung selbst.
/// - "Nicht authentifiziert" ist eine fachliche Antwort. Ist die BC-Anbindung oder BC nicht erreichbar, führt das zu
///   <see cref="BcLoginOutcome.Unavailable"/>: Dann gilt der lokale Stand des letzten BC-Logins (Offline-Modus).
/// - Das PIN verlässt diesen Dienst nur Richtung BC-Anbindung und steht nie im Log. BC wird nur gefragt, wenn der Username aus Ziffern
///   besteht (Personalnummer) oder der User schon BC-geführt ist — Logins wie "admin" gehen nie an BC.
///
/// Nach jedem BC-Erfolg wird das PIN als bcrypt-Hash (<see cref="User.Password"/>, so greift der Offline-Fallback über die normale lokale
/// Prüfung) und reversibel verschlüsselt (<see cref="User.BcPinEncrypted"/>) abgelegt.
/// </summary>
public class BcLoginService(
    IBcGateway gateway,
    IOptions<AuthOptions> authOptions,
    PinProtector pinProtector,
    PasswordService passwords,
    AccessDbContext db,
    ILogger<BcLoginService> logger)
{
    public bool IsEnabled => gateway.IsConfigured && pinProtector.IsConfigured;

    public async Task<BcLoginResult> TryLoginAsync(string username, string pin, User? existingUser, CancellationToken ct)
    {
        var bcManaged = existingUser?.BcPinEncrypted is not null;
        if (!IsEnabled || string.IsNullOrWhiteSpace(pin) || (!bcManaged && !IsPersonnelNumber(username)))
            return new BcLoginResult(BcLoginOutcome.NotApplicable);

        BcEmployeeCheck check;
        try
        {
            check = await gateway.VerifyEmployeeAsync(username, pin, ct);
        }
        catch (BcGatewayUnavailableException ex)
        {
            // Bewusst nur der Typ: Meldungen/Details dürfen nie Inhalte der Anfrage (PIN) enthalten.
            logger.LogWarning("BC-Anmeldung nicht möglich ({ErrorType}) — Fallback auf lokalen Stand.", ex.GetType().Name);
            return new BcLoginResult(BcLoginOutcome.Unavailable);
        }

        if (!check.Authenticated || string.IsNullOrWhiteSpace(check.EmployeeName))
            return new BcLoginResult(BcLoginOutcome.Rejected);

        var user = existingUser;
        if (user is null)
        {
            if (!authOptions.Value.BcAutoCreateUsers)
                return new BcLoginResult(BcLoginOutcome.Rejected);

            user = new User { Username = username };
            db.Users.Add(user);
        }

        // bcrypt ist absichtlich teuer: nur neu hashen, wenn sich das PIN in BC geändert hat.
        if (!passwords.Verify(pin, user.Password))
            user.Password = passwords.Hash(pin);
        if (user.BcPinEncrypted is null || pinProtector.Unprotect(user.BcPinEncrypted) != pin)
            user.BcPinEncrypted = pinProtector.Protect(pin);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Parallel-Login derselben Personalnummer hat den User gerade angelegt — der andere Request gewinnt.
            db.ChangeTracker.Clear();
            user = await db.Users.FirstAsync(u => u.Username == username, ct);
        }

        return user.Active
            ? new BcLoginResult(BcLoginOutcome.Authenticated, user)
            : new BcLoginResult(BcLoginOutcome.Rejected);
    }

    private static bool IsPersonnelNumber(string username) =>
        username.Length > 0 && username.All(char.IsAsciiDigit);
}
