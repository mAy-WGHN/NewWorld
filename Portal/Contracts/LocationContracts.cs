namespace Portal.Contracts;

/// <summary>Eine Filiale, wie Rollen-Rechte sie liefert (dort liegen die Stammdaten); in Meldungen und Statistiken mit Namen angezeigt.</summary>
public record LocationResponse(Guid Id, string Name, string StoreCode, bool Archived);
