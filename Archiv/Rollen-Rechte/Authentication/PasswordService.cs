using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using BCryptNet = BCrypt.Net.BCrypt;

namespace Rollen_Rechte.Authentication;

/// <summary>
/// Neue Hashes: bcrypt. Verifiziert werden die beiden Formate des Symfony-Hashers ("auto"):
/// bcrypt ($2y$/$2a$/$2b$) und argon2id ($argon2id$v=19$m=..,t=..,p=..$salt$hash). So bleiben alle
/// bestehenden Passwörter gültig und Symfony kann bcrypt-Hashes weiterhin prüfen (Rückfallebene).
/// </summary>
public class PasswordService(IConfiguration configuration)
{
    // Für unbekannte User ebenfalls einen Hash prüfen, damit die Antwortzeit nichts verrät.
    private static readonly string DummyHash = BCryptNet.HashPassword("dummy-password-for-timing", 10);

    private readonly int _bcryptCost = configuration.GetValue("Auth:BcryptCost", 12);

    public string Hash(string password) => BCryptNet.HashPassword(password, _bcryptCost);

    public bool Verify(string password, string? hash)
    {
        try
        {
            if (hash is null)
            {
                BCryptNet.Verify(password, DummyHash);
                return false;
            }

            return hash.StartsWith("$argon2id$", StringComparison.Ordinal)
                ? VerifyArgon2Id(password, hash)
                : BCryptNet.Verify(password, hash);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool VerifyArgon2Id(string password, string phc)
    {
        // $argon2id$v=19$m=65536,t=4,p=1$<salt b64>$<hash b64>   (Base64 ohne Padding)
        var parts = phc.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "argon2id") return false;

        var parameters = parts[2].Split(',')
            .Select(p => p.Split('='))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => int.Parse(p[1]));
        var salt = FromBase64(parts[3]);
        var expected = FromBase64(parts[4]);

        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = parameters["m"],
            Iterations = parameters["t"],
            DegreeOfParallelism = parameters["p"]
        };
        return CryptographicOperations.FixedTimeEquals(argon.GetBytes(expected.Length), expected);
    }

    private static byte[] FromBase64(string value) =>
        Convert.FromBase64String(value.PadRight(value.Length + (4 - value.Length % 4) % 4, '='));
}
