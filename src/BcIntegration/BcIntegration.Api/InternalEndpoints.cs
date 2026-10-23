using BcIntegration.Core.MasterData;
using Shared.Authorization;

namespace BcIntegration.Api;

/// <summary>Internal endpoints. Each one requires its own service app role; there are no endpoints for frontends.</summary>
public static class InternalEndpoints
{
    public static WebApplication MapInternalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/internal");

        // Publishes EmployeeDataErasureRequested to all services with copies and stops further copies of the person.
        group.MapPost("/employees/{employeeId:guid}/erasure", async (Guid employeeId, EmployeeErasure erasure, CancellationToken cancellationToken) =>
                await erasure.RequestAsync(employeeId, cancellationToken) ? Results.Accepted() : Results.NotFound())
            .RequireAuthorization(AppPolicies.ServiceRole(BcIntegrationRoles.EmployeeErasureRequest));

        return app;
    }
}
