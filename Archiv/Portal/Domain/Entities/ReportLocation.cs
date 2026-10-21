namespace Portal.Domain.Entities;

/// <summary>
/// Verbindung Meldung ↔ Filiale (Tabelle <c>report_location</c>). Die Filialen gehören Rollen-Rechte: Hier steht nur ihre ID, ohne Fremdschlüssel
/// (Namen kommen über <see cref="Services.ILocationDirectory"/>).
/// </summary>
public class ReportLocation
{
    public Guid ReportId { get; set; }
    public Guid LocationId { get; set; }
}
