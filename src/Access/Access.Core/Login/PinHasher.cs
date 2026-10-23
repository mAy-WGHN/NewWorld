using System.Security.Cryptography;

namespace Access.Core.Login;

/// <summary>
/// Salted PBKDF2-SHA512 hash of a PIN. Format <c>pbkdf2-sha512$iterations$salt$hash</c> (Base64), so the iterations can be raised
/// later and old hashes still verify. A PIN has little entropy: the hash only slows down guessing after a database leak, the
/// real protection is BC itself and the throttling.
/// </summary>
public sealed class PinHasher(int iterations)
{
    private const string Prefix = "pbkdf2-sha512";
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA512, HashSize);
        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string pin, string stored)
    {
        if (!TryParse(stored, out var storedIterations, out var salt, out var expected))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, storedIterations, HashAlgorithmName.SHA512, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>The stored hash was made with fewer iterations than configured now (or is unreadable).</summary>
    public bool NeedsRehash(string stored) => !TryParse(stored, out var storedIterations, out _, out _) || storedIterations < iterations;

    private static bool TryParse(string stored, out int storedIterations, out byte[] salt, out byte[] hash)
    {
        storedIterations = 0;
        salt = [];
        hash = [];
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out storedIterations) || storedIterations < 1)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
            return salt.Length > 0 && hash.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
