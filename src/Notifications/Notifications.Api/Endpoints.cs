using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Notifications.Core;
using Notifications.Core.Delivery;
using Notifications.Core.Templates;
using Notifications.Infrastructure;
using Shared.Authorization;

namespace Notifications.Api;

/// <summary>
/// "My notifications" and "my preferences". Every call concerns the signed-in person's own data only: the person comes from the token,
/// never from the request, and another person's notification looks like a missing one (404).
/// </summary>
public static class Endpoints
{
    private const int DefaultTake = 50;
    private const int MaxTake = 100;

    public static WebApplication MapNotificationEndpoints(this WebApplication app)
    {
        var me = app.MapGroup("/api/me");

        me.MapGet("/notifications", async (ClaimsPrincipal user, NotificationsDbContext db, bool? unread, int? take, DateTimeOffset? before, CancellationToken ct) =>
            {
                if (Me(user) is not { } employeeId)
                {
                    return Results.Forbid();
                }

                var query = db.InApp.AsNoTracking().Where(n => n.EmployeeId == employeeId);
                if (unread == true)
                {
                    query = query.Where(n => n.ReadAt == null);
                }

                if (before is { } limit)
                {
                    query = query.Where(n => n.CreatedAt < limit);
                }

                var rows = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(Math.Clamp(take ?? DefaultTake, 1, MaxTake)).ToListAsync(ct);
                return Results.Ok(rows.Select(n => new NotificationDto(n.Id, n.TemplateKey, n.Title, n.Body, n.DeepLink, n.CreatedAt, n.ReadAt)));
            })
            .RequireAuthorization(AppPolicies.Privilege(NotificationPrivileges.ReadOwn));

        me.MapGet("/notifications/unread-count", async (ClaimsPrincipal user, NotificationsDbContext db, CancellationToken ct) =>
                Me(user) is not { } employeeId
                    ? Results.Forbid()
                    : Results.Ok(new UnreadCountDto(await db.InApp.CountAsync(n => n.EmployeeId == employeeId && n.ReadAt == null, ct))))
            .RequireAuthorization(AppPolicies.Privilege(NotificationPrivileges.ReadOwn));

        me.MapPost("/notifications/{id:guid}/read", async (Guid id, ClaimsPrincipal user, NotificationsDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                if (Me(user) is not { } employeeId)
                {
                    return Results.Forbid();
                }

                var now = time.GetUtcNow();
                var changed = await db.InApp.Where(n => n.Id == id && n.EmployeeId == employeeId && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
                return changed > 0 || await db.InApp.AnyAsync(n => n.Id == id && n.EmployeeId == employeeId, ct) ? Results.NoContent() : Results.NotFound();
            })
            .RequireAuthorization(AppPolicies.Privilege(NotificationPrivileges.UpdateOwn));

        me.MapPost("/notifications/read-all", async (ClaimsPrincipal user, NotificationsDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                if (Me(user) is not { } employeeId)
                {
                    return Results.Forbid();
                }

                var now = time.GetUtcNow();
                await db.InApp.Where(n => n.EmployeeId == employeeId && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(AppPolicies.Privilege(NotificationPrivileges.UpdateOwn));

        me.MapGet("/notification-preferences", async (ClaimsPrincipal user, NotificationsDbContext db, TemplateCatalog catalog, CancellationToken ct) =>
            {
                if (Me(user) is not { } employeeId)
                {
                    return Results.Forbid();
                }

                var chosen = await db.Preferences.AsNoTracking().Where(p => p.EmployeeId == employeeId).ToListAsync(ct);
                return Results.Ok(catalog.All.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => new NotificationTypeDto(
                    t.Key,
                    t.Mandatory,
                    Channels.Select(c => new ChannelPreferenceDto(
                        c.ToString(),
                        t.Mandatory || chosen.FirstOrDefault(p => p.TemplateKey == t.Key && p.Channel == c.ToString())?.Enabled != false)).ToList())));
            })
            .RequireAuthorization(AppPolicies.Privilege(NotificationPrivileges.PreferenceReadOwn));

        me.MapPut("/notification-preferences/{templateKey}/{channel}", async (
                string templateKey, string channel, SetPreferenceRequest request, ClaimsPrincipal user, PreferenceService preferences, CancellationToken ct) =>
            {
                if (Me(user) is not { } employeeId)
                {
                    return Results.Forbid();
                }

                if (!Enum.TryParse<NotificationChannel>(channel, ignoreCase: true, out var parsed))
                {
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: nameof(PreferenceResult.InvalidChannel));
                }

                return await preferences.SetAsync(employeeId, templateKey, parsed, request.Enabled, ct) switch
                {
                    PreferenceResult.Saved => Results.NoContent(),
                    PreferenceResult.UnknownType => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: nameof(PreferenceResult.UnknownType)),
                    PreferenceResult.Mandatory => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: nameof(PreferenceResult.Mandatory)),
                    _ => Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: nameof(PreferenceResult.InvalidChannel)),
                };
            })
            .RequireAuthorization(AppPolicies.Privilege(NotificationPrivileges.PreferenceUpdateOwn));

        return app;
    }

    private static readonly NotificationChannel[] Channels = [NotificationChannel.InApp, NotificationChannel.Push, NotificationChannel.Email];

    private static Guid? Me(ClaimsPrincipal user) => Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var id) ? id : null;
}
