using BcIntegration.Core.Errors;
using BcIntegration.Core.Login;
using BcIntegration.Core.MasterData;
using Shared.Authorization;

namespace BcIntegration.Api;

/// <summary>Internal endpoints. Each one requires its own service role; there are no endpoints for frontends.</summary>
public static class InternalEndpoints
{
    private const int MaxPinLength = 64;

    public static WebApplication MapInternalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/internal");

        // Publishes EmployeeDataErasureRequested to all services with copies and stops further copies of the person.
        group.MapPost("/employees/{employeeId:guid}/erasure", async (Guid employeeId, EmployeeErasure erasure, CancellationToken cancellationToken) =>
                await erasure.RequestAsync(employeeId, cancellationToken) ? Results.Accepted() : Results.NotFound())
            .RequireAuthorization(AppPolicies.ServiceRole(BcIntegrationRoles.EmployeeErasureRequest));

        // BC compares the PIN; the answer is only yes/no and the name. No body, PIN or error text is ever logged or returned.
        group.MapPost("/employees/verify-pin", async (VerifyPinRequest request, IBcPinVerifier verifier, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.PersonnelNumber) || string.IsNullOrWhiteSpace(request.Pin) || request.Pin.Length > MaxPinLength)
                {
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "InvalidRequest");
                }

                try
                {
                    var check = await verifier.VerifyAsync(request.PersonnelNumber, request.Pin, cancellationToken);
                    return Results.Ok(new VerifyPinResponse(check.Authenticated, check.EmployeeName));
                }
                catch (Exception ex) when (ex is BcTransientFailureException or BcTechnicalFailureException)
                {
                    // "BC unavailable" must be distinguishable from "BC says no": the caller falls back to its last known state.
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "BcUnavailable");
                }
            })
            .RequireAuthorization(AppPolicies.ServiceRole(BcIntegrationRoles.PinVerify));

        return app;
    }
}

public sealed record VerifyPinRequest(string PersonnelNumber, string Pin);

public sealed record VerifyPinResponse(bool Authenticated, string? EmployeeName);
