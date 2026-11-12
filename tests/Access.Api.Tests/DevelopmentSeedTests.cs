using Access.Core.Acl;
using Access.Core.Responsibilities;
using Access.Infrastructure;
using Access.Infrastructure.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Testing;
using static Access.Api.Tests.AccessApi;

namespace Access.Api.Tests;

/// <summary>The local development seed: roles from the role file and an HR team that is responsible for everybody. Without the settings nothing happens.</summary>
public sealed class DevelopmentSeedTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static string WriteRoleFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rollen-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "customPrivileges": [],
              "roles": [
                { "name": "Public User", "privileges": [ "absence:read-own" ] },
                { "name": "Personalabteilung", "privileges": [ "absence:approve", "absence:read-reason" ] },
                { "name": "Filialleitung", "privileges": [ "logbook:write" ] }
              ]
            }
            """);
        return path;
    }

    private static async Task<T> InDbAsync<T>(AccessApi api, Func<AccessDbContext, Task<T>> read)
    {
        using var scope = api.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AccessDbContext>());
    }

    [Fact]
    public async Task The_configured_people_become_the_HR_team_with_the_roles_of_the_file()
    {
        var file = WriteRoleFile();
        try
        {
            await using var api = await StartAsync(postgres, settings: new Dictionary<string, string>
            {
                ["Access:DevelopmentSeed:RoleFile"] = file,
                ["Access:DevelopmentSeed:HrPersonnelNumbers:0"] = "10042", // Erika
            });

            var roles = await InDbAsync(api, db => db.Roles.ToListAsync());
            var hr = roles.Single(r => r.Name == "Personalabteilung");
            hr.Privileges.ShouldBe(["absence:approve", "absence:read-reason"], ignoreOrder: true);
            roles.Single(r => r.Id == Role.PublicUserId).Privileges.ShouldBe(["absence:read-own"]); // the built-in role was empty and is filled

            var assignments = await InDbAsync(api, db => db.Assignments.Where(a => a.EmployeeId == Erika && a.RoleId == hr.Id).ToListAsync());
            assignments.ShouldHaveSingleItem().Unrestricted.ShouldBeTrue();

            var responsibilities = await InDbAsync(api, db => db.Responsibilities.Where(r => r.ResponsibleEmployeeId == Erika).ToListAsync());
            responsibilities.Select(r => r.Topic).ShouldBe(DevelopmentSeeder.HrTopics, ignoreOrder: true);
            responsibilities.ShouldAllBe(r => r.TargetKind == nameof(ResponsibilityTarget.EveryoneTarget));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Running_again_adds_nothing_and_keeps_changes_made_by_hand()
    {
        var file = WriteRoleFile();
        try
        {
            await using var api = await StartAsync(postgres, settings: new Dictionary<string, string>
            {
                ["Access:DevelopmentSeed:RoleFile"] = file,
                ["Access:DevelopmentSeed:HrPersonnelNumbers:0"] = "10042",
            });
            using (var scope = api.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
                (await db.Roles.SingleAsync(r => r.Name == "Personalabteilung")).Privileges = ["absence:approve"]; // changed by hand
                await db.SaveChangesAsync();
            }

            await api.ReceiveAsync(Tom, "10002", "Tom Tester", F018); // another recalculation runs the seed again

            (await InDbAsync(api, db => db.Roles.CountAsync(r => r.Name == "Personalabteilung"))).ShouldBe(1);
            (await InDbAsync(api, db => db.Roles.SingleAsync(r => r.Name == "Personalabteilung"))).Privileges.ShouldBe(["absence:approve"]);
            (await InDbAsync(api, db => db.Assignments.CountAsync(a => a.EmployeeId == Erika && a.Source == nameof(AssignmentSource.Seed)))).ShouldBe(1);
            (await InDbAsync(api, db => db.Responsibilities.CountAsync(r => r.ResponsibleEmployeeId == Erika))).ShouldBe(DevelopmentSeeder.HrTopics.Length);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Demo_people_get_their_role_everywhere_or_only_for_the_main_store_and_what_is_unknown_is_skipped()
    {
        var file = WriteRoleFile();
        try
        {
            await using var api = await StartAsync(postgres, settings: new Dictionary<string, string>
            {
                ["Access:DevelopmentSeed:RoleFile"] = file,
                ["Access:DevelopmentSeed:Assignments:0"] = "10001:Filialleitung:home", // Max, only the own store
                ["Access:DevelopmentSeed:Assignments:1"] = "10042:Filialleitung", // Erika, everywhere
                ["Access:DevelopmentSeed:Assignments:2"] = "99999:Filialleitung", // nobody has this number (yet)
                ["Access:DevelopmentSeed:Assignments:3"] = "10002:Unknown role",
                ["Access:DevelopmentSeed:Assignments:4"] = "broken",
            });
            await api.ReceiveAsync(Tom, "10002", "Tom Tester", F018); // another recalculation runs the seed again: nothing doubles

            var role = (await InDbAsync(api, db => db.Roles.SingleAsync(r => r.Name == "Filialleitung")));
            var seeded = await InDbAsync(api, db => db.Assignments.Where(a => a.RoleId == role.Id).ToListAsync());
            seeded.Count.ShouldBe(2);
            var max = seeded.Single(a => a.EmployeeId == Max);
            max.Unrestricted.ShouldBeFalse();
            max.IncludeHomeStore.ShouldBeTrue();
            seeded.Single(a => a.EmployeeId == Erika).Unrestricted.ShouldBeTrue();
            seeded.ShouldAllBe(a => a.Source == nameof(AssignmentSource.Seed));
            (await InDbAsync(api, db => db.Assignments.AnyAsync(a => a.EmployeeId == Tom && a.Source == nameof(AssignmentSource.Seed)))).ShouldBeFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Without_the_settings_nothing_is_seeded()
    {
        await using var api = await StartAsync(postgres);

        (await InDbAsync(api, db => db.Roles.AnyAsync(r => r.Name == "Personalabteilung"))).ShouldBeFalse();
        (await InDbAsync(api, db => db.Responsibilities.AnyAsync())).ShouldBeFalse();
        (await InDbAsync(api, db => db.Assignments.AnyAsync(a => a.Source == nameof(AssignmentSource.Seed)))).ShouldBeFalse();
    }
}
