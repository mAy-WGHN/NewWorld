using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Benachrichtigungen.Domain.Enums;

namespace Benachrichtigungen.Contracts;

/// <summary>
/// Umschlag eines Ereignisses (<c>POST /api/events</c>): <c>id</c> vergibt der Absender (wiederholte Zustellung mit derselben ID wird ignoriert),
/// <c>type</c> bestimmt, wie <c>data</c> gelesen wird. Der Absender ergibt sich aus seinem API-Schlüssel.
/// </summary>
public record EventRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public Guid? Id { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(100, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 100 Zeichen enthalten.")]
    public string? Type { get; init; }

    public DateTime? OccurredAt { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public JsonElement? Data { get; init; }
}

/// <summary>Daten von <c>report.created</c>: Das Portal meldet, welche Meldung neu ist. Nur Fakten, kein Text; Titel und Link baut der Dienst.</summary>
public record ReportCreatedData
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public Guid? ReportId { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public ReportCategory? Category { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public ReportSeverity? Severity { get; init; }

    /// <summary>Die Beschreibung der Meldung; sie ist der Text der Benachrichtigung.</summary>
    public string? Description { get; init; }

    /// <summary>Die Filialen der Meldung: Wer eine davon im Scope hat, darf sie sehen und wird benachrichtigt (eine genügt).</summary>
    public List<Guid>? LocationIds { get; init; }
}

/// <summary><c>Duplicate</c> = diese Ereignis-ID war schon verarbeitet; <c>Recipients</c> = Zahl der benachrichtigten User.</summary>
public record EventResult(bool Duplicate, int Recipients);
