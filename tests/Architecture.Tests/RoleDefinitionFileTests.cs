using System.Text.Json;

namespace Architecture.Tests;

/// <summary>
/// The role file in the repository (<c>docs/rollen.json</c>, decision 44) is kept up to date with the privileges of the services:
/// every privilege in it exists, so a typo or a removed privilege is found here and not when the file is imported.
/// </summary>
public class RoleDefinitionFileTests
{
    private static readonly string FilePath = Path.Combine(Solution.Root, "docs", "rollen.json");

    private static JsonElement Load() => JsonDocument.Parse(File.ReadAllText(FilePath)).RootElement;

    private static List<(string Name, List<string> Privileges)> Roles() =>
        Load().GetProperty("roles").EnumerateArray()
            .Select(r => (r.GetProperty("name").GetString()!, r.GetProperty("privileges").EnumerateArray().Select(p => p.GetString()!).ToList()))
            .ToList();

    /// <summary>Privileges known to the services plus the custom ones the file declares itself.</summary>
    private static HashSet<string> KnownKeys()
    {
        var keys = PrivilegeCatalogs.All.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var custom in Load().GetProperty("customPrivileges").EnumerateArray())
        {
            keys.Add(custom.ValueKind == JsonValueKind.String ? custom.GetString()! : custom.GetProperty("key").GetString()!);
        }

        return keys;
    }

    [Fact]
    public void The_file_has_the_supported_version_and_roles()
    {
        Load().GetProperty("version").GetInt32().ShouldBe(1);
        Roles().ShouldNotBeEmpty();
    }

    [Fact]
    public void Every_privilege_in_the_file_exists_in_the_catalogue_of_a_service()
    {
        var known = KnownKeys();
        var unknown = Roles()
            .SelectMany(r => r.Privileges.Where(p => !Matches(p, known)).Select(p => $"{r.Name}: {p}"))
            .ToList();

        unknown.ShouldBeEmpty("fix the typo or remove the privilege from docs/rollen.json");
    }

    [Fact]
    public void Role_names_are_unique_and_the_built_in_administrator_is_not_in_the_file()
    {
        var names = Roles().Select(r => r.Name).ToList();

        names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ShouldBeEmpty();
        names.ShouldNotContain(n => n.Equals("Administrator", StringComparison.OrdinalIgnoreCase));
        names.ShouldNotContain(n => n.Equals("Former Employee", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_role_lists_a_privilege_twice()
    {
        Roles().SelectMany(r => r.Privileges.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => $"{r.Name}: {g.Key}")).ShouldBeEmpty();
    }

    [Fact]
    public void Public_user_is_given_to_everybody_and_holds_only_rights_over_the_own_data()
    {
        var publicUser = Roles().Single(r => r.Name == "Public User");

        publicUser.Privileges.Where(p => !p.EndsWith("-own", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    /// <summary>A wildcard (<c>*</c> or <c>area:*</c>) counts as known when it covers at least one privilege.</summary>
    private static bool Matches(string privilege, HashSet<string> known) =>
        privilege == "*" ? known.Count > 0
        : privilege.EndsWith(":*", StringComparison.Ordinal) ? known.Any(k => k.StartsWith(privilege[..^1], StringComparison.Ordinal))
        : known.Contains(privilege);
}
