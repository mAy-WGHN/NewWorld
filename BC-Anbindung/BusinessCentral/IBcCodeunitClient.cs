namespace BC_Anbindung.BusinessCentral;

/// <summary>
/// Ruft LS-Central-Codeunit-Web-Services von Business Central auf (SOAP 1.1, document/literal — OData v4
/// liefert für Codeunits 404). Austauschbar, damit Login und Store-Sync in Tests ohne BC laufen.
/// </summary>
public interface IBcCodeunitClient
{
    /// <summary>
    /// Ruft eine Operation auf und liefert den Inhalt des "return_value"-Elements (JSON-String). Nur für
    /// Codeunits, die so antworten (bestätigt: "GetStores" als DynDataSet, "Zeiterfassung"/GetStatus als
    /// Erfolg/Fehler-JSON).
    /// </summary>
    /// <param name="codeunitName">Service-Name wie in BC veröffentlicht, z. B. "GetStores".</param>
    /// <param name="operationName">Exportierte Funktion, z. B. "GetStores" oder "GetStatus".</param>
    /// <param name="parameters">Parameter in der exakten Reihenfolge des WSDL-Sequence-Schemas.</param>
    Task<string> CallAsync(
        string codeunitName, string operationName, IEnumerable<(string Name, string Value)> parameters, CancellationToken ct);
}
