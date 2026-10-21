using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Benachrichtigungen.Authentication;
using Benachrichtigungen.Common.Api;
using Benachrichtigungen.Contracts;
using Benachrichtigungen.Data;
using Benachrichtigungen.Domain.Entities;
using Benachrichtigungen.Serialization;
using Benachrichtigungen.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Benachrichtigungen.Controllers;

/// <summary>
/// Eingang für Ereignisse der anderen Anwendungen (Portal, HR-Tool, …): "es ist etwas passiert". Dieser Dienst entscheidet, wer davon erfährt und wie.
/// Zustellung mindestens einmal: Dieselbe Ereignis-ID wird nur einmal verarbeitet. Ist "Rollen-Rechte" nicht erreichbar, antwortet der Dienst mit 503 und merkt sich
/// das Ereignis nicht — der Absender sendet es später noch einmal.
/// </summary>
[Route("api/events")]
[Authorize(AuthenticationSchemes = SourceKeyDefaults.Scheme)]
public class EventsController(NotificationsDbContext db, ReportCreatedHandler reportCreated) : ApiControllerBase
{
    private static readonly JsonSerializerOptions DataJson = CreateDataOptions();

    private static JsonSerializerOptions CreateDataOptions()
    {
        var options = new JsonSerializerOptions();
        ApiJson.Configure(options);
        options.PropertyNameCaseInsensitive = true;
        return options;
    }

    [HttpPost]
    public async Task<IActionResult> Receive(EventRequest request, CancellationToken ct)
    {
        var id = request.Id!.Value;
        var source = User.Identity!.Name!;
        if (await db.ProcessedEvents.AnyAsync(e => e.Id == id, ct)) return Success(new EventResult(Duplicate: true, Recipients: 0));

        int recipients;
        switch (request.Type)
        {
            case ReportCreatedHandler.EventType:
                var data = ReadData<ReportCreatedData>(request.Data!.Value);
                try
                {
                    recipients = await reportCreated.HandleAsync(data, ct);
                }
                catch (RecipientDirectoryUnavailableException)
                {
                    throw new ApiException(StatusCodes.Status503ServiceUnavailable,
                        "Die Empfängerermittlung (Rollen-Rechte) ist nicht erreichbar. Das Ereignis bitte später erneut senden.");
                }

                break;
            default:
                throw ApiException.Validation("type", $"Unbekannter Ereignistyp: {request.Type}");
        }

        db.ProcessedEvents.Add(new ProcessedEvent { Id = id, Type = request.Type!, Source = source, Recipients = recipients });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Dasselbe Ereignis wurde parallel verarbeitet: der andere Aufruf war schneller.
            return Success(new EventResult(Duplicate: true, Recipients: 0));
        }

        return Success(new EventResult(Duplicate: false, recipients));
    }

    /// <summary>Liest und prüft die Daten eines Ereignisses; fehlende oder unlesbare Felder ergeben 422 (der Absender kann es nicht durch Wiederholen beheben).</summary>
    private static T ReadData<T>(JsonElement element) where T : class
    {
        T? data;
        try
        {
            data = element.Deserialize<T>(DataJson);
        }
        catch (JsonException)
        {
            throw ApiException.Validation("data", "Die Daten des Ereignisses sind ungültig.");
        }

        if (data is null) throw ApiException.Validation("data", "Die Daten des Ereignisses sind ungültig.");

        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(data, new ValidationContext(data), results, validateAllProperties: true))
        {
            var errors = results.SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""])
                    .Select(member => new ApiError("data." + (member.Length == 0 ? "" : char.ToLowerInvariant(member[0]) + member[1..]), r.ErrorMessage ?? "Der Wert ist ungültig.")))
                .ToList();
            throw new ApiException(StatusCodes.Status422UnprocessableEntity, "Validierung fehlgeschlagen.", errors);
        }

        return data;
    }
}
