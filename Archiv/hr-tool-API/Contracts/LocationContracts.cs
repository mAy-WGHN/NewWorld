namespace HrTool_API.Contracts;

/// <summary>Eine Filiale, wie Rollen-Rechte sie liefert (dort liegen die Stammdaten); zur Anzeige mit Namen.</summary>
public record LocationResponse(Guid Id, string Name, string StoreCode, bool Archived);
