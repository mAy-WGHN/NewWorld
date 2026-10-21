namespace Benachrichtigungen.Domain.Entities;

/// <summary>
/// Ein bereits verarbeitetes Ereignis: Ereignisse werden mindestens einmal zugestellt, ein wiederholtes Ereignis (gleiche ID) wird deshalb ignoriert,
/// statt noch einmal zu benachrichtigen.
/// </summary>
public class ProcessedEvent
{
    /// <summary>Die vom Absender vergebene Ereignis-ID.</summary>
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    /// <summary>Der Absender (aus seinem API-Schlüssel), z. B. "portal".</summary>
    public string Source { get; set; } = string.Empty;

    public int Recipients { get; set; }

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
