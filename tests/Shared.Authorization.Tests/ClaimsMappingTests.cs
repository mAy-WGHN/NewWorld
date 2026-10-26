using System.Security.Claims;
using Shared.Authorization;

namespace Shared.Authorization.Tests;

public class ClaimsMappingTests
{
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");

    private static async Task<ClaimsPrincipal> Transform(params Claim[] claims)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return await new AppClaimsTransformation().TransformAsync(principal);
    }

    [Fact]
    public async Task Employee_id_of_the_access_token_becomes_the_internal_employee_claim()
    {
        var principal = await Transform(new Claim("employee_id", Anna.ToString()));

        principal.FindFirst(AppClaimTypes.EmployeeId)!.Value.ShouldBe(Anna.ToString());
    }

    [Fact]
    public async Task Malformed_employee_id_is_ignored()
    {
        var principal = await Transform(new Claim("employee_id", "not-a-guid"));

        principal.FindFirst(AppClaimTypes.EmployeeId).ShouldBeNull();
    }

    [Fact]
    public async Task A_roles_claim_in_a_token_never_becomes_a_service_role()
    {
        var principal = await Transform(new Claim("roles", "BcIntegration.EmployeeErasure.Request"), new Claim("roles", "Executive"));

        principal.FindAll(AppClaimTypes.ServiceRole).ShouldBeEmpty();
    }

    [Fact]
    public async Task Object_id_does_not_identify_a_person()
    {
        var principal = await Transform(new Claim("oid", Anna.ToString()));

        principal.FindFirst(AppClaimTypes.EmployeeId).ShouldBeNull();
    }

    [Fact]
    public async Task Transformation_is_idempotent()
    {
        var transformation = new AppClaimsTransformation();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("employee_id", Anna.ToString())], "test"));

        var twice = await transformation.TransformAsync(await transformation.TransformAsync(principal));

        twice.FindAll(AppClaimTypes.EmployeeId).Count().ShouldBe(1);
    }
}
