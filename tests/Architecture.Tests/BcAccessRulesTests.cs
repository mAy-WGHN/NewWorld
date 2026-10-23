using System.Text.RegularExpressions;

namespace Architecture.Tests;

/// <summary>BC only through the BC Integration service: no other service has BC client code or BC configuration.</summary>
public partial class BcAccessRulesTests
{
    [GeneratedRegex(@"businesscentral\.dynamics\.com|GetSection\(\s*""Bc""|""Bc:|Bc__|""Bc""\s*:|\bBcClient\b|BusinessCentral", RegexOptions.IgnoreCase)]
    private static partial Regex BcAccess();

    private static bool OutsideBcIntegration(string relativePath) =>
        !relativePath.StartsWith($"BcIntegration{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    [Fact]
    public void Bc_client_types_exist_only_in_bc_integration()
    {
        Solution.AllServiceAssemblies()
            .Where(a => !a.GetName().Name!.StartsWith("BcIntegration.", StringComparison.Ordinal))
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Namespace?.Contains("BusinessCentral", StringComparison.Ordinal) == true || t.Name.StartsWith("BcClient", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ShouldBeEmpty();
    }

    [Fact]
    public void No_other_service_or_shared_library_contains_bc_code_or_configuration()
    {
        var settings = Directory.EnumerateFiles(Solution.SrcPath, "appsettings*.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(Solution.SrcPath, f), File.ReadAllLines(f)));

        var violations = Solution.SourceFiles().Concat(settings)
            .Where(f => OutsideBcIntegration(f.Item1))
            // The AppHost wires the BC fake into BC Integration only.
            .Where(f => !f.Item1.StartsWith($"AppHost{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => f.Item2.Select((line, i) => (File: f.Item1, Line: i + 1, Text: line)))
            .Where(l => BcAccess().IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}")
            .ToList();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void Rule_detects_bc_access()
    {
        BcAccess().IsMatch("""http.BaseAddress = new Uri("https://api.businesscentral.dynamics.com/v2.0/");""").ShouldBeTrue();
        BcAccess().IsMatch("""configuration.GetSection("Bc")""").ShouldBeTrue();
        BcAccess().IsMatch("""  "Bc": { "BaseUrl": "x" }""").ShouldBeTrue();
        BcAccess().IsMatch("""var x = new BcSyncSucceeded(...);""").ShouldBeFalse();
    }
}
