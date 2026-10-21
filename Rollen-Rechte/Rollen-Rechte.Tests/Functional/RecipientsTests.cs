using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Rollen_Rechte.Domain.Entities;
using Rollen_Rechte.Tests.Infrastructure;

namespace Rollen_Rechte.Tests.Functional;

/// <summary>"Wer darf diesen Vorgang sehen?" — für andere Anwendungen, z. B. den Benachrichtigungs-Dienst.</summary>
public class RecipientsTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static readonly DateTime Past = DateTime.UtcNow.AddDays(-2);
    private static readonly DateTime Future = DateTime.UtcNow.AddDays(2);

    /// <summary>Registriert ein scopbares Recht "katXXXX:read" (eigener Bereich je Test) und liefert den Schlüssel.</summary>
    private async Task<string> ScopableRightAsync()
    {
        var area = "kat" + Guid.NewGuid().ToString("N")[..8];
        var response = await SendAsync(HttpMethod.Put, $"/api/apps/{ApiFactory.AppKey}/catalog", new
        {
            areas = new[] { new { key = area, label = "Test", actions = new[] { new { privilege = $"{area}:read", label = "Lesen", scopable = true } } } }
        }, configure: r => r.Headers.Add("X-Api-Key", ApiFactory.AppApiKey));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return $"{area}:read";
    }

    private Task<Guid> ScopeOfAsync(params Guid[] locationIds) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var scope = new Scope { Name = Unique("Scope"), Members = [.. locationIds.Select(id => new ScopeMember { Dimension = ScopeDimensions.Location, RefId = id })] };
            db.Scopes.Add(scope);
            await db.SaveChangesAsync();
            return scope.Id;
        });

    private Task<Guid> ReportScopeAsync(Guid reportId) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var scope = new Scope { Name = Unique("Scope"), Members = [new ScopeMember { Dimension = ScopeDimensions.Report, RefId = reportId }] };
            db.Scopes.Add(scope);
            await db.SaveChangesAsync();
            return scope.Id;
        });

    /// <summary>User mit einer Zuweisung der Rolle (Rechte) — uneingeschränkt (<c>scopeIds = null</c>) oder auf Scopes beschränkt.</summary>
    private async Task<User> UserAsync(string privilege, Guid[]? scopeIds = null, bool homeScope = false, Guid? home = null,
        DateTime? validFrom = null, DateTime? validTo = null, bool active = true)
    {
        var user = await CreateAdminAsync(withAdminRole: false, active: active, configure: u => u.HomeLocationId = home);
        await Factory.WithDbAsync(async (db, _) =>
        {
            var role = new AclRole { Name = Unique("Rolle"), Privileges = [privilege] };
            db.AclRoles.Add(role);
            db.AclUserRoles.Add(new AclUserRole
            {
                UserId = user.Id, AclRoleId = role.Id, Scoped = scopeIds is not null, HomeScope = homeScope, ValidFrom = validFrom, ValidTo = validTo,
                Scopes = [.. (scopeIds ?? []).Select(sid => new AclUserRoleScope { UserId = user.Id, AclRoleId = role.Id, ScopeId = sid })]
            });
            await db.SaveChangesAsync();
        });
        return user;
    }

    private async Task<List<Guid>> ResolveAsync(object body, string appKey = ApiFactory.AppKey, string? apiKey = ApiFactory.AppApiKey)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/apps/{appKey}/recipients", body, configure: r =>
        {
            if (apiKey is not null) r.Headers.Add("X-Api-Key", apiKey);
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await DataAsync(response)).GetProperty("userIds").EnumerateArray().Select(u => u.GetGuid()).ToList();
    }

    [Fact]
    public async Task NeedsTheApplicationsKey()
    {
        var body = new { privilege = "x:read" };

        foreach (var (appKey, key) in new[] { (ApiFactory.AppKey, (string?)null), (ApiFactory.AppKey, "falsch"), ("hr", ApiFactory.AppApiKey) })
        {
            var response = await SendAsync(HttpMethod.Post, $"/api/apps/{appKey}/recipients", body, configure: r =>
            {
                if (key is not null) r.Headers.Add("X-Api-Key", key);
            });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task ValidatesTheRequest()
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/apps/{ApiFactory.AppKey}/recipients", new { }, configure: r => r.Headers.Add("X-Api-Key", ApiFactory.AppApiKey));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task UnrestrictedAssignments_AlwaysCount_AndWildcardRolesToo()
    {
        var right = await ScopableRightAsync();
        var a = await CreateLocationAsync();
        var unrestricted = await UserAsync(right);
        var wildcard = await UserAsync("*");
        var without = await UserAsync("faq:read");

        var result = await ResolveAsync(new { privilege = right, locationIds = new[] { a.Id } });

        Assert.Contains(unrestricted.Id, result);
        Assert.Contains(wildcard.Id, result);
        Assert.DoesNotContain(without.Id, result);
    }

    [Fact]
    public async Task ScopedAssignments_CountWhenOneOfTheLocationsIsInTheScope()
    {
        var right = await ScopableRightAsync();
        var (a, b, c) = (await CreateLocationAsync(), await CreateLocationAsync(), await CreateLocationAsync());
        var inA = await UserAsync(right, [await ScopeOfAsync(a.Id)]);
        var inRegion = await UserAsync(right, [await ScopeOfAsync(b.Id, c.Id)]);
        var inC = await UserAsync(right, [await ScopeOfAsync(c.Id)]);

        var result = await ResolveAsync(new { privilege = right, locationIds = new[] { a.Id, b.Id } }); // eine Filiale genügt

        Assert.Contains(inA.Id, result);
        Assert.Contains(inRegion.Id, result);
        Assert.DoesNotContain(inC.Id, result);
    }

    [Fact]
    public async Task HomeScope_UsesTheHomeLocation()
    {
        var right = await ScopableRightAsync();
        var (a, b) = (await CreateLocationAsync(), await CreateLocationAsync());
        var home = await UserAsync(right, [], homeScope: true, home: a.Id);
        var other = await UserAsync(right, [], homeScope: true, home: b.Id);
        var none = await UserAsync(right, [], homeScope: true);

        var result = await ResolveAsync(new { privilege = right, locationIds = new[] { a.Id } });

        Assert.Contains(home.Id, result);
        Assert.DoesNotContain(other.Id, result);
        Assert.DoesNotContain(none.Id, result);
    }

    [Fact]
    public async Task ASingleObjectScope_CountsForThatObjectOnly()
    {
        var right = await ScopableRightAsync();
        var (shared, other) = (Guid.NewGuid(), Guid.NewGuid());
        var user = await UserAsync(right, [await ReportScopeAsync(shared)]);

        Assert.Contains(user.Id, await ResolveAsync(new { privilege = right, reportIds = new[] { shared } }));
        Assert.DoesNotContain(user.Id, await ResolveAsync(new { privilege = right, reportIds = new[] { other } }));
    }

    [Fact]
    public async Task WithoutLocationsOrObjects_OnlyUnrestrictedAssignmentsCount()
    {
        var right = await ScopableRightAsync();
        var a = await CreateLocationAsync();
        var unrestricted = await UserAsync(right);
        var scoped = await UserAsync(right, [await ScopeOfAsync(a.Id)]);

        var result = await ResolveAsync(new { privilege = right });

        Assert.Contains(unrestricted.Id, result);
        Assert.DoesNotContain(scoped.Id, result);
    }

    [Fact]
    public async Task ExpiredFutureAndInactive_AreLeftOut()
    {
        var right = await ScopableRightAsync();
        var valid = await UserAsync(right, validFrom: Past, validTo: Future);
        var expired = await UserAsync(right, validFrom: Past.AddDays(-5), validTo: Past);
        var future = await UserAsync(right, validFrom: Future);
        var inactive = await UserAsync(right, active: false);

        var result = await ResolveAsync(new { privilege = right });

        Assert.Contains(valid.Id, result);
        Assert.DoesNotContain(expired.Id, result);
        Assert.DoesNotContain(future.Id, result);
        Assert.DoesNotContain(inactive.Id, result);
    }

    [Fact]
    public async Task ARestrictedAssignmentOfANonScopableRight_DoesNotCount()
    {
        var a = await CreateLocationAsync();
        var scoped = await UserAsync("custom_privilege:read", [await ScopeOfAsync(a.Id)]);
        var unrestricted = await UserAsync("custom_privilege:read");

        var result = await ResolveAsync(new { privilege = "custom_privilege:read", locationIds = new[] { a.Id } });

        Assert.Contains(unrestricted.Id, result);
        Assert.DoesNotContain(scoped.Id, result);
    }

    [Fact]
    public async Task UserIds_NarrowTheCandidates()
    {
        var right = await ScopableRightAsync();
        var first = await UserAsync(right);
        var second = await UserAsync(right);

        var result = await ResolveAsync(new { privilege = right, userIds = new[] { first.Id, Guid.NewGuid() } });

        Assert.Equal([first.Id], result);
        Assert.DoesNotContain(second.Id, result);
    }

    [Fact]
    public async Task AUserWithSeveralAssignments_IsListedOnce()
    {
        var right = await ScopableRightAsync();
        var (a, b) = (await CreateLocationAsync(), await CreateLocationAsync());
        var user = await UserAsync(right, [await ScopeOfAsync(a.Id)]);
        await Factory.WithDbAsync(async (db, _) =>
        {
            var role = new AclRole { Name = Unique("Rolle"), Privileges = [right] };
            var scope = new Scope { Name = Unique("Scope"), Members = [new ScopeMember { Dimension = ScopeDimensions.Location, RefId = b.Id }] };
            db.AclRoles.Add(role);
            db.Scopes.Add(scope);
            db.AclUserRoles.Add(new AclUserRole
            {
                UserId = user.Id, AclRoleId = role.Id, Scoped = true,
                Scopes = [new AclUserRoleScope { UserId = user.Id, AclRoleId = role.Id, ScopeId = scope.Id }]
            });
            await db.SaveChangesAsync();
        });

        var result = await ResolveAsync(new { privilege = right, locationIds = new[] { a.Id, b.Id } });

        Assert.Single(result, user.Id);
    }
}
