using Rollen_Rechte.Authentication;
using Rollen_Rechte.Data;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Commands;

/// <summary>
/// "dotnet Rollen-Rechte.dll decrypt-pin &lt;username&gt;": zeigt das verschlüsselt gespeicherte BC-PIN eines Users im Klartext.
/// Braucht denselben Auth__PinEncryptionKey wie die API; das PIN landet nur in dieser Konsolenausgabe.
/// </summary>
public static class DecryptPinCommand
{
    public const string Name = "decrypt-pin";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output)
    {
        var username = args.FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(username))
        {
            await output.WriteLineAsync($"Aufruf: {Name} <username>");
            return 2;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<PinProtector>();

        var encrypted = await db.Users.Where(u => u.Username == username).Select(u => u.BcPinEncrypted).FirstOrDefaultAsync();
        if (encrypted is null)
        {
            await output.WriteLineAsync($"Benutzer \"{username}\" existiert nicht oder hat kein gespeichertes BC-PIN.");
            return 1;
        }

        var pin = protector.Unprotect(encrypted);
        if (pin is null)
        {
            await output.WriteLineAsync("Entschlüsselung fehlgeschlagen: Auth__PinEncryptionKey fehlt oder ist nicht der Schlüssel, mit dem verschlüsselt wurde.");
            return 1;
        }

        await output.WriteLineAsync($"PIN von \"{username}\": {pin}");
        return 0;
    }
}
