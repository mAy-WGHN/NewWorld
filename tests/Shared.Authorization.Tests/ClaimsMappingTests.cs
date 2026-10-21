using System.Security.Claims;
using Shared.Authorization;

namespace Shared.Authorization.Tests;

public class ClaimsMappingTests
{
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");

    private static async Task<ClaimsPrincipal> Transform(InMemoryPermissionCache cache, params Claim[] claims)
    {
        var transformation = new AppClaimsTransformation(cache);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return await transformation.TransformAsync(principal);
    }

    [Fact]
    public async Task Provider_roles_are_mapped_to_internal_roles()
    {
        var principal = await Transform(new InMemoryPermissionCache(),
            new Claim("roles", "StoreManager"), new Claim("roles", "Employee"));

        principal.FindAll(AppClaimTypes.Role).Select(c => c.Value).ShouldBe(["StoreManager", "Employee"], ignoreOrder: true);
    }

    [Fact]
    public async Task Unknown_provider_roles_are_dropped()
    {
        var principal = await Transform(new InMemoryPermissionCache(),
            new Claim("roles", "Domain.Admin"), new Claim("roles", "Hr"));

        principal.FindAll(AppClaimTypes.Role).Select(c => c.Value).ShouldBe(["Hr"]);
    }

    [Fact]
    public async Task Object_id_is_resolved_to_the_employee_id_via_the_cache()
    {
        var cache = new InMemoryPermissionCache();
        cache.Apply(new PermissionSnapshot(Anna, 1, "oid-anna", new HashSet<Guid>(), new HashSet<Guid>()));

        var principal = await Transform(cache, new Claim("oid", "oid-anna"));

        principal.FindFirst(AppClaimTypes.EmployeeId)!.Value.ShouldBe(Anna.ToString());
    }

    [Fact]
    public async Task Unresolvable_object_id_yields_no_employee_claim()
    {
        var principal = await Transform(new InMemoryPermissionCache(), new Claim("oid", "oid-unknown"));

        principal.FindFirst(AppClaimTypes.EmployeeId).ShouldBeNull();
    }

    [Fact]
    public async Task Transformation_is_idempotent()
    {
        var cache = new InMemoryPermissionCache();
        cache.Apply(new PermissionSnapshot(Anna, 1, "oid-anna", new HashSet<Guid>(), new HashSet<Guid>()));
        var transformation = new AppClaimsTransformation(cache);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "oid-anna"), new Claim("roles", "Hr")], "test"));

        var once = await transformation.TransformAsync(principal);
        var twice = await transformation.TransformAsync(once);

        twice.FindAll(AppClaimTypes.Role).Count().ShouldBe(1);
        twice.FindAll(AppClaimTypes.EmployeeId).Count().ShouldBe(1);
    }
}
