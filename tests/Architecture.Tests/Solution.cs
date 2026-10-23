using System.Reflection;

namespace Architecture.Tests;

internal static class Solution
{
    public static readonly string[] Services = ["BcIntegration", "Access", "Notifications", "Workforce", "Portal"];

    public static readonly string[] Layers = ["Api", "Core", "Infrastructure", "Contracts"];

    public static Assembly Load(string service, string layer) => Assembly.Load($"{service}.{layer}");

    public static IEnumerable<Assembly> All(string layer) => Services.Select(s => Load(s, layer));

    public static IEnumerable<Assembly> AllServiceAssemblies() =>
        Services.SelectMany(s => Layers.Select(l => Load(s, l)));

    private static readonly Lazy<string> RootPath = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NewWorld.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("NewWorld.slnx not found above the test directory.");
    });

    public static string SrcPath => Path.Combine(RootPath.Value, "src");

    /// <summary>Hand-written C# files under src/ (without bin/obj and generated EF migrations), path relative to src/.</summary>
    public static IEnumerable<(string RelativePath, string[] Lines)> SourceFiles() =>
        Directory.EnumerateFiles(SrcPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(SrcPath, f), File.ReadAllLines(f)));
}
