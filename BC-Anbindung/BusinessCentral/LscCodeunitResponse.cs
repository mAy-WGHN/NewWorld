using System.Text.Json;
using System.Xml.Linq;

namespace BC_Anbindung.BusinessCentral;

/// <summary>
/// Hilfsfunktionen für die SOAP-Antworten der LS-Central-Codeunit-Web-Services. "GetStores" liefert einen
/// JSON-String im "return_value"-Element (generisches "DynDataSet"-Format, <see cref="GetDataSetRows"/>).
/// Andere Codeunits antworten mit verschachteltem XML — vor dem Verwenden einer neuen Codeunit das
/// tatsächliche Format live prüfen.
/// </summary>
public static class LscCodeunitResponse
{
    /// <summary>Liest den Text des "return_value"-Elements aus dem SOAP-1.1-Antwort-Envelope.</summary>
    public static string ExtractReturnValue(string soapXml)
    {
        var doc = XDocument.Parse(soapXml);
        var element = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "return_value")
            ?? throw new InvalidOperationException("SOAP-Antwort enthält kein 'return_value'-Element.");
        return element.Value;
    }

    /// <summary>
    /// Extrahiert die Zeilen eines benannten "DynDataSet" aus dem JSON-Rückgabewert. Felder werden pro
    /// Zeile über "FieldIndex" adressiert — deshalb wird zuerst FieldIndex→FieldName aus "DataSetFields"
    /// aufgelöst, bevor die Zeilen gebaut werden.
    /// </summary>
    public static List<Dictionary<string, string>> GetDataSetRows(string returnValueJson, string dataSetName)
    {
        using var doc = JsonDocument.Parse(returnValueJson);
        var dataSet = FindDataSet(doc.RootElement, dataSetName)
            ?? throw new InvalidOperationException($"DataSet '{dataSetName}' nicht in der Antwort gefunden.");

        var fieldNames = dataSet.GetProperty("DataSetFields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("FieldIndex").GetInt32(), f => f.GetProperty("FieldName").GetString()!);

        var rows = new List<Dictionary<string, string>>();
        foreach (var row in dataSet.GetProperty("DataSetRows").EnumerateArray())
        {
            var values = new Dictionary<string, string>();
            foreach (var field in row.GetProperty("Fields").EnumerateArray())
            {
                var index = field.GetProperty("FieldIndex").GetInt32();
                values[fieldNames[index]] = ReadAsString(field.GetProperty("FieldValue"));
            }
            rows.Add(values);
        }
        return rows;
    }

    /// <summary>"FieldValue" ist je nach BC-Feldtyp ein JSON-String, eine Zahl oder ein Bool.</summary>
    private static string ReadAsString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Null => "",
        _ => value.GetRawText()
    };

    private static JsonElement? FindDataSet(JsonElement element, string dataSetName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("DynDataSet", out var dynDataSet)
                    && dynDataSet.TryGetProperty("DataSetName", out var name)
                    && name.GetString() == dataSetName)
                    return dynDataSet;

                foreach (var prop in element.EnumerateObject())
                {
                    var found = FindDataSet(prop.Value, dataSetName);
                    if (found is not null) return found;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindDataSet(item, dataSetName);
                    if (found is not null) return found;
                }
                break;
        }
        return null;
    }
}
