using System.Security.Claims;
using Shared.Authorization;

namespace Shared.Authorization.Tests;

public class ClaimsMappingTests
{
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");

    private static async Task<ClaimsPrincipal> Transform(SharedAuthorizationOptions options, params Claim[] claims)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return await new AppClaimsTransformation(options).TransformAsync(principal);
    }

    private static SharedAuthorizationOptions WithServiceRole(string role)
    {
        var options = new SharedAuthorizationOptions();
        options.ServiceRoles.Add(role);
        return options;
    }

    [Fact]
    public async Task Employee_id_of_the_access_token_becomes_the_internal_employee_claim()
    {
        var principal = await Transform(new SharedAuthorizationOptions(), new Claim("employee_id", Anna.ToString()));

        principal.FindFirst(AppClaimTypes.EmployeeId)!.Value.ShouldBe(Anna.ToString());
    }

    [Fact]
    public async Task Malformed_employee_id_is_ignored()
    {
        var principal = await Transform(new SharedAuthorizationOptions(), new Claim("employee_id", "not-a-guid"));

        principal.FindFirst(AppClaimTypes.EmployeeId).ShouldBeNull();
    }

    [Fact]
    public async Task Configured_service_roles_are_mapped_and_others_dropped()
    {
        var principal = await Transform(WithServiceRole("BcIntegration.EmployeeErasure.Request"),
            new Claim("roles", "BcIntegration.EmployeeErasure.Request"), new Claim("roles", "Other.Service.Role"), new Claim("roles", "Executive"));

        principal.FindAll(AppClaimTypes.ServiceRole).Select(c => c.Value).ShouldBe(["BcIntegration.EmployeeErasure.Request"]);
    }

    [Fact]
    public async Task Entra_object_id_no_longer_identifies_a_person()
    {
        var principal = await Transform(new SharedAuthorizationOptions(), new Claim("oid", Anna.ToString()));

        principal.FindFirst(AppClaimTypes.EmployeeId).ShouldBeNull();
    }

    [Fact]
    public async Task Transformation_is_idempotent()
    {
        var transformation = new AppClaimsTransformation(WithServiceRole("X.Y"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("employee_id", Anna.ToString()), new Claim("roles", "X.Y")], "test"));

        var twice = await transformation.TransformAsync(await transformation.TransformAsync(principal));

        twice.FindAll(AppClaimTypes.EmployeeId).Count().ShouldBe(1);
        twice.FindAll(AppClaimTypes.ServiceRole).Count().ShouldBe(1);
    }
}
