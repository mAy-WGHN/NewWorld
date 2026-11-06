using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;

namespace Shared.Infrastructure.Audit;

/// <summary>One line of the audit log as the API shows it; <see cref="Changes"/> only with the privilege to read values.</summary>
public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Service,
    string ActorKind,
    string ActorPersonnelNumber,
    string ActorName,
    string Action,
    string EntityType,
    string EntityId,
    string? Changes);

public static class AuditEndpoints
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 1000;

    /// <summary>
    /// <c>GET /api/audit</c>: newest first, paged by <c>before</c> (the <c>occurredAt</c> of the last line seen). The existence of the log is not revealed:
    /// everybody without <paramref name="readPrivilege"/> (also anonymous callers) gets the same <c>404</c> as for any unknown path.
    /// The changed values are only included for holders of <paramref name="readValuesPrivilege"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapAuditEndpoints<TContext>(
        this IEndpointRouteBuilder app, string service, string readPrivilege, string readValuesPrivilege) where TContext : DbContext
    {
        var handler = new AuditListHandler<TContext>(service, readPrivilege, readValuesPrivilege);
        app.MapGet("/api/audit", (RequestDelegate)handler.HandleAsync).AllowAnonymous();
        return app;
    }

    private sealed class AuditListHandler<TContext>(string service, string readPrivilege, string readValuesPrivilege) where TContext : DbContext
    {
        public async Task HandleAsync(HttpContext context)
        {
            await (await HandleCoreAsync(context.Request, context.User, context.RequestServices.GetRequiredService<IPermissionCache>(), context.RequestServices.GetRequiredService<TContext>(), context)
                ).ExecuteAsync(context);
        }

        private async Task<IResult> HandleCoreAsync(
            HttpRequest request, ClaimsPrincipal user, IPermissionCache permissions, TContext db, HttpContext context)
        {
            if (!TryGetPermissions(user, permissions, out var snapshot) || !snapshot.Has(readPrivilege))
            {
                return Results.NotFound();
            }

            var withValues = snapshot.Has(readValuesPrivilege);
            var query = db.Set<AuditEntryRecord>().AsNoTracking().AsQueryable();
            if (DateTimeOffset.TryParse(request.Query["from"], CultureInfo.InvariantCulture, out var from))
            {
                query = query.Where(e => e.OccurredAt >= from);
            }

            if (DateTimeOffset.TryParse(request.Query["to"], CultureInfo.InvariantCulture, out var to))
            {
                query = query.Where(e => e.OccurredAt <= to);
            }

            if (DateTimeOffset.TryParse(request.Query["before"], CultureInfo.InvariantCulture, out var before))
            {
                query = query.Where(e => e.OccurredAt < before);
            }

            if (request.Query["personnelNumber"].ToString() is { Length: > 0 } number)
            {
                query = query.Where(e => e.ActorPersonnelNumber == number);
            }

            if (Enum.TryParse<AuditAction>(request.Query["action"], true, out var action))
            {
                query = query.Where(e => e.Action == action);
            }

            if (request.Query["entityType"].ToString() is { Length: > 0 } entityType)
            {
                query = query.Where(e => e.EntityType == entityType);
            }

            var limit = int.TryParse(request.Query["limit"], CultureInfo.InvariantCulture, out var requested) ? Math.Clamp(requested, 1, MaxLimit) : DefaultLimit;
            var rows = await query.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Take(limit).ToListAsync(context.RequestAborted);
            return Results.Ok(rows.Select(e => new AuditEntryDto(
                e.Id, e.OccurredAt, service, e.ActorKind.ToString(), e.ActorPersonnelNumber, e.ActorName,
                e.Action.ToString(), e.EntityType, e.EntityId, withValues ? e.ChangesJson : null)));
        }
    }

    private static bool TryGetPermissions(ClaimsPrincipal user, IPermissionCache cache, out PermissionSnapshot snapshot)
    {
        snapshot = null!;
        if (user.FindFirst(AppClaimTypes.EmployeeId)?.Value is { } value
            && Guid.TryParse(value, out var employeeId)
            && cache.TryGet(employeeId, out var found)
            && found is not null)
        {
            snapshot = found;
            return true;
        }

        return false;
    }
}
