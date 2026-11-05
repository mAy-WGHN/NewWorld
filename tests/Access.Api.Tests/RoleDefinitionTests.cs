using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Access.Api;
using Access.Infrastructure.Acl;
using Shared.Testing;
using static Access.Api.Tests.AccessApi;

namespace Access.Api.Tests;

/// <summary>Roles as a file: export, comparison with the existing roles (diff) and import of the selected items.</summary>
public sealed class RoleDefinitionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static Func<HttpRequestMessage, HttpRequestMessage> As(Guid person) => r => r.AsUser(person);

    private static RoleDefinitionDocument File(IReadOnlyList<RoleDefinition> roles, IReadOnlyList<CustomPrivilegeDefinition>? custom = null) =>
        new(RoleDefinitionService.CurrentVersion, custom ?? [], roles);

    private static async Task<RoleImportResult> ImportAsync(AccessApi api, RoleDefinitionDocument document, bool apply, string[]? selected = null)
    {
        using var response = await api.SendAsync(HttpMethod.Post, "/api/role-definitions/import" + (apply ? "?apply=true" : string.Empty), As(Admin), new ImportRolesRequest(document, selected));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RoleImportResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    private static async Task<AccessApi> StartWithCatalogAsync(PostgresFixture postgres)
    {
        var api = await StartAsync(postgres);
        await api.RegisterWorkforceCatalogAsync();
        return api;
    }

    [Fact]
    public async Task The_export_has_the_roles_with_sorted_privileges_but_not_the_administrator()
    {
        await using var api = await StartWithCatalogAsync(postgres);
        await api.CreateAsync("/api/roles", new CreateRoleRequest("Filialleitung", ["absence:read", "absence:approve"]), As(Admin));

        var file = await api.GetAsync<RoleDefinitionDocument>("/api/role-definitions", As(Admin));

        file.Version.ShouldBe(1);
        file.Roles.ShouldNotContain(r => r.Name == "Administrator");
        file.Roles.ShouldContain(r => r.Name == "Public User");
        file.Roles.Single(r => r.Name == "Filialleitung").Privileges.ShouldBe(["absence:approve", "absence:read"]);
    }

    [Fact]
    public async Task The_export_has_the_own_privileges()
    {
        await using var api = await StartWithCatalogAsync(postgres);
        await api.SendAsync(HttpMethod.Post, "/api/custom-privileges", As(Admin), new CreateCustomPrivilegeRequest("sonder:lesen", true, "Sonderrecht"));

        var file = await api.GetAsync<RoleDefinitionDocument>("/api/role-definitions", As(Admin));

        file.CustomPrivileges.ShouldBe([new CustomPrivilegeDefinition("sonder:lesen", true, "Sonderrecht")]);
    }

    [Fact]
    public async Task A_comparison_shows_the_diff_and_changes_nothing()
    {
        await using var api = await StartWithCatalogAsync(postgres);
        await api.CreateAsync("/api/roles", new CreateRoleRequest("Filialleitung", ["absence:read", "absence:approve"]), As(Admin));

        var result = await ImportAsync(api, File([new RoleDefinition("Filialleitung", ["absence:read", "absence:read-own"]), new RoleDefinition("Neu", ["absence:read"])]), apply: false);

        result.Applied.ShouldBeFalse();
        result.HasErrors.ShouldBeFalse();
        var update = result.Items.Single(i => i.Name == "Filialleitung");
        update.Action.ShouldBe("update");
        update.Added.ShouldBe(["absence:read-own"]);
        update.Removed.ShouldBe(["absence:approve"]);
        result.Items.Single(i => i.Name == "Neu").Action.ShouldBe("create");
        var roles = await api.GetAsync<List<RoleDto>>("/api/roles", As(Admin));
        roles.ShouldNotContain(r => r.Name == "Neu");
        roles.Single(r => r.Name == "Filialleitung").Privileges.Order().ShouldBe(["absence:approve", "absence:read"]);
    }

    [Fact]
    public async Task Applying_creates_missing_roles_and_sets_the_privileges_of_existing_ones_by_name()
    {
        await using var api = await StartWithCatalogAsync(postgres);
        await api.CreateAsync("/api/roles", new CreateRoleRequest("Filialleitung", ["absence:read", "absence:approve"]), As(Admin));
        await api.CreateAsync("/api/roles", new CreateRoleRequest("Nur hier", ["absence:read"]), As(Admin));

        var result = await ImportAsync(api, File([new RoleDefinition("filialleitung", ["absence:read-own"]), new RoleDefinition("Neu", ["absence:*"])]), apply: true);

        result.Applied.ShouldBeTrue();
        var roles = await api.GetAsync<List<RoleDto>>("/api/roles", As(Admin));
        roles.Single(r => r.Name == "Filialleitung").Privileges.ShouldBe(["absence:read-own"]);
        roles.Single(r => r.Name == "Neu").Privileges.ShouldBe(["absence:*"]);
        roles.ShouldContain(r => r.Name == "Nur hier");
    }

    [Fact]
    public async Task Only_the_selected_items_are_applied()
    {
        await using var api = await StartWithCatalogAsync(postgres);
        await api.CreateAsync("/api/roles", new CreateRoleRequest("Filialleitung", ["absence:read"]), As(Admin));

        var result = await ImportAsync(api, File([new RoleDefinition("Filialleitung", ["absence:approve"]), new RoleDefinition("Neu", ["absence:read"])]), apply: true, selected: ["Neu"]);

        result.Applied.ShouldBeTrue();
        var roles = await api.GetAsync<List<RoleDto>>("/api/roles", As(Admin));
        roles.Single(r => r.Name == "Filialleitung").Privileges.ShouldBe(["absence:read"]);
        roles.ShouldContain(r => r.Name == "Neu");
    }

    [Fact]
    public async Task An_own_privilege_in_the_file_is_created_and_can_be_used_by_a_role_of_the_same_file()
    {
        await using var api = await StartWithCatalogAsync(postgres);

        var result = await ImportAsync(api, File([new RoleDefinition("Revisor", ["sonder:lesen"])], [new CustomPrivilegeDefinition("sonder:lesen", false, "Sonderrecht")]), apply: true);

        result.Applied.ShouldBeTrue();
        (await api.GetAsync<List<CatalogEntryDto>>("/api/catalog", As(Admin))).ShouldContain(c => c.Key == "sonder:lesen" && c.Owner == "custom");
        (await api.GetAsync<List<RoleDto>>("/api/roles", As(Admin))).Single(r => r.Name == "Revisor").Privileges.ShouldBe(["sonder:lesen"]);
    }

    [Fact]
    public async Task A_file_with_an_unknown_privilege_is_refused_as_a_whole()
    {
        await using var api = await StartWithCatalogAsync(postgres);

        var result = await ImportAsync(api, File([new RoleDefinition("Gut", ["absence:read"]), new RoleDefinition("Schlecht", ["gibt:es-nicht"])]), apply: true);

        result.Applied.ShouldBeFalse();
        result.HasErrors.ShouldBeTrue();
        result.Items.Single(i => i.Name == "Schlecht").Error!.ShouldStartWith("UnknownPrivilege");
        (await api.GetAsync<List<RoleDto>>("/api/roles", As(Admin))).ShouldNotContain(r => r.Name == "Gut");
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("administrator")]
    public async Task The_administrator_cannot_be_changed_by_a_file(string name)
    {
        await using var api = await StartWithCatalogAsync(postgres);

        var result = await ImportAsync(api, File([new RoleDefinition(name, ["absence:read"])]), apply: true);

        result.HasErrors.ShouldBeTrue();
        result.Items.Single().Error.ShouldBe("BuiltInRole");
    }

    [Fact]
    public async Task A_file_of_another_version_or_with_a_double_name_is_refused()
    {
        await using var api = await StartWithCatalogAsync(postgres);

        var version = await ImportAsync(api, new RoleDefinitionDocument(99, [], []), apply: false);
        var twice = await ImportAsync(api, File([new RoleDefinition("A", ["absence:read"]), new RoleDefinition("a", ["absence:read"])]), apply: false);

        version.Items.Single().Error.ShouldBe("UnsupportedVersion");
        twice.Items.ShouldContain(i => i.Error == "Duplicate");
    }

    [Fact]
    public async Task Importing_the_export_of_the_same_system_changes_nothing()
    {
        await using var api = await StartWithCatalogAsync(postgres);
        await api.CreateAsync("/api/roles", new CreateRoleRequest("Filialleitung", ["absence:read", "absence:approve"]), As(Admin));
        var file = await api.GetAsync<RoleDefinitionDocument>("/api/role-definitions", As(Admin));

        var result = await ImportAsync(api, file, apply: false);

        result.HasErrors.ShouldBeFalse();
        result.Items.ShouldAllBe(i => i.Action == "unchanged");
    }

    [Fact]
    public async Task Without_the_privileges_for_roles_nothing_can_be_exported_or_imported()
    {
        await using var api = await StartWithCatalogAsync(postgres);

        using var export = await api.SendAsync(HttpMethod.Get, "/api/role-definitions", As(Erika));
        using var import = await api.SendAsync(HttpMethod.Post, "/api/role-definitions/import", As(Erika), new ImportRolesRequest(File([]), null));
        using var anonymous = await api.SendAsync(HttpMethod.Get, "/api/role-definitions", identity: null);

        export.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        import.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
