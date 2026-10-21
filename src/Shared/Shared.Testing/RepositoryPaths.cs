namespace Shared.Testing;

public static class RepositoryPaths
{
    private static readonly Lazy<string> RootPath = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NewWorld.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("NewWorld.slnx not found above the test directory.");
    });

    /// <summary>Repository root (the directory containing <c>NewWorld.slnx</c>).</summary>
    public static string Root => RootPath.Value;
}
