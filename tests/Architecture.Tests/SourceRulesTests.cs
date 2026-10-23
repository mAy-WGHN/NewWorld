using System.Text.RegularExpressions;

namespace Architecture.Tests;

public partial class SourceRulesTests
{
    [GeneratedRegex(@"\b(DateTime|DateTimeOffset)\s*\.\s*(Now|UtcNow|Today)\b")]
    private static partial Regex ClockAccess();

    [GeneratedRegex("(Claim\\(|FindFirst\\(|FindAll\\(|HasClaim\\(|RequireClaim\\()\\s*\"(oid|roles|employee_id)\"")]
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
    public void Token_claims_are_only_touched_in_shared_authorization()
    {
        var sep = Path.DirectorySeparatorChar;
        var violations = Solution.SourceFiles()
            .Where(f => !f.RelativePath.StartsWith($"Shared{sep}Shared.Authorization{sep}", StringComparison.Ordinal)
                     && !f.RelativePath.StartsWith($"Shared{sep}Shared.Testing{sep}", StringComparison.Ordinal))
            .SelectMany(f => f.Lines.Select((line, i) => (f.RelativePath, Line: i + 1, Text: line)))
            .Where(l => !IsComment(l.Text) && ProviderClaimLiteral().IsMatch(l.Text))
            .Select(l => $"{l.RelativePath}:{l.Line}")
            .ToList();

        violations.ShouldBeEmpty("token claims belong to Shared.Authorization (use app:employee_id / app:service_role)");
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
        ProviderClaimLiteral().IsMatch("""new Claim("employee_id", id)""").ShouldBeTrue();
        ProviderClaimLiteral().IsMatch("""new Claim("app:employee_id", id)""").ShouldBeFalse();
        ProviderClaimLiteral().IsMatch("""b.ToTable("roles");""").ShouldBeFalse();
    }
}
