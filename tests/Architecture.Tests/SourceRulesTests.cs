using System.Text.RegularExpressions;

namespace Architecture.Tests;

public partial class SourceRulesTests
{
    [GeneratedRegex(@"\b(DateTime|DateTimeOffset)\s*\.\s*(Now|UtcNow|Today)\b")]
    private static partial Regex ClockAccess();

    [GeneratedRegex("\"(oid|roles)\"")]
    private static partial Regex ProviderClaimLiteral();

    private static bool IsComment(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    [Fact]
    public void Clock_is_only_accessed_through_TimeProvider()
    {
        var violations = Solution.SourceFiles()
            .SelectMany(f => f.Lines.Select((line, i) => (f.RelativePath, Line: i + 1, Text: line)))
            .Where(l => !IsComment(l.Text) && ClockAccess().IsMatch(l.Text))
            .Select(l => $"{l.RelativePath}:{l.Line}")
            .ToList();

        violations.ShouldBeEmpty("use TimeProvider instead of DateTime.Now/UtcNow/DateTimeOffset.UtcNow");
    }

    [Fact]
    public void Provider_claims_oid_and_roles_are_only_touched_in_shared_authorization()
    {
        var sep = Path.DirectorySeparatorChar;
        var violations = Solution.SourceFiles()
            .Where(f => !f.RelativePath.StartsWith($"Shared{sep}Shared.Authorization{sep}", StringComparison.Ordinal)
                     && !f.RelativePath.StartsWith($"Shared{sep}Shared.Testing{sep}", StringComparison.Ordinal))
            .SelectMany(f => f.Lines.Select((line, i) => (f.RelativePath, Line: i + 1, Text: line)))
            .Where(l => !IsComment(l.Text) && ProviderClaimLiteral().IsMatch(l.Text))
            .Select(l => $"{l.RelativePath}:{l.Line}")
            .ToList();

        violations.ShouldBeEmpty("claims 'oid'/'roles' belong to Shared.Authorization (use app:role / app:employee_id)");
    }

    [Fact]
    public void Clock_rule_detects_violations()
    {
        ClockAccess().IsMatch("var x = DateTime.UtcNow;").ShouldBeTrue();
        ClockAccess().IsMatch("var x = DateTimeOffset.UtcNow;").ShouldBeTrue();
        ClockAccess().IsMatch("var x = DateTime.Now ;").ShouldBeTrue();
        ClockAccess().IsMatch("var x = time.GetUtcNow();").ShouldBeFalse();
    }

    [Fact]
    public void Claim_rule_detects_violations()
    {
        ProviderClaimLiteral().IsMatch("""principal.FindFirst("oid")""").ShouldBeTrue();
        ProviderClaimLiteral().IsMatch("""new Claim("roles", r)""").ShouldBeTrue();
        ProviderClaimLiteral().IsMatch("""new Claim("app:role", r)""").ShouldBeFalse();
    }
}
