using Benachrichtigungen.Authentication;
using Benachrichtigungen.Common.Api;
using Benachrichtigungen.Contracts;
using Benachrichtigungen.Data;
using Benachrichtigungen.Domain.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Benachrichtigungen.Controllers;

[Route("api/me")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class PushSubscriptionsController(NotificationsDbContext db, IConfiguration configuration) : ApiControllerBase
{
    [HttpGet("push-public-key")]
    public IActionResult PublicKey()
    {
        var key = configuration["Vapid:PublicKey"];
        if (string.IsNullOrEmpty(key))
            throw new ApiException(StatusCodes.Status503ServiceUnavailable, "Web Push ist nicht konfiguriert.");
        return Success(new PushPublicKeyResponse(key));
    }

    // Idempotent über den Endpoint: ein bestehender Eintrag gehört danach dem aufrufenden User
    // (nötig, wenn sich zwei Admins denselben Browser teilen).
    [HttpPost("push-subscriptions")]
    public async Task<IActionResult> Subscribe(CreatePushSubscriptionRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var endpoint = request.Endpoint!;
        var subscription = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);

        if (subscription is null)
        {
            subscription = new PushSubscription { Endpoint = endpoint };
            db.PushSubscriptions.Add(subscription);
        }

        subscription.UserId = userId;
        subscription.PublicKey = request.Keys!.P256dh!;
        subscription.AuthToken = request.Keys.Auth!;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Paralleles Abonnieren desselben Endpoints: der andere Request war schneller.
        }

        return CreatedEmpty();
    }

    // Löscht nur eigene Abos; 204 auch, wenn nichts (oder nichts Eigenes) existiert.
    [HttpDelete("push-subscriptions")]
    public async Task<IActionResult> Unsubscribe(DeletePushSubscriptionRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var endpoint = request.Endpoint!;
        await db.PushSubscriptions.Where(s => s.Endpoint == endpoint && s.UserId == userId).ExecuteDeleteAsync(ct);
        return NoContent();
    }
}
