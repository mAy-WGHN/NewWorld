namespace Portal.Core.CashRegisters;

/// <summary>
/// The short sign of a person in the cash register list ("Anna von Schmidt" becomes "AvS"): the first letter of every name part, a particle written in
/// lower case ("von", "van", "de") stays lower case. Hyphens and commas separate parts. Without a name a question mark.
/// </summary>
public static class CashRegisterInitials
{
    public static string Of(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var parts = name.Split([' ', '-', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Select(p => IsParticle(p) ? char.ToLowerInvariant(p[0]) : char.ToUpperInvariant(p[0])));
    }

    // a particle is a whole word without any capital letter and at most three letters ("von", "van", "de", "zu", "da")
    private static bool IsParticle(string part) => part.Length <= 3 && part.All(char.IsLower) && part.Length > 1;
}
