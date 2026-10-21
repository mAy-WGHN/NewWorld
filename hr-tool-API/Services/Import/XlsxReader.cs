using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace HrTool_API.Services.Import;

/// <summary>
/// Liest das erste Blatt einer .xlsx-Datei (BC-Exporte) als Textzeilen, ohne zusätzliche Bibliothek. Unterstützt gemeinsame und eingebettete Zeichenketten sowie Zahlen
/// (als Text, wie sie in der Datei stehen: Datum = Excel-Seriennummer, Dezimalzeichen = Punkt). Die erste Zeile ist die Kopfzeile.
/// </summary>
public static class XlsxReader
{
    public static XlsxTable ReadFirstSheet(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var shared = ReadSharedStrings(zip);
        var entry = zip.GetEntry("xl/worksheets/sheet1.xml") ?? throw new InvalidDataException($"{Path.GetFileName(path)}: Das Blatt xl/worksheets/sheet1.xml fehlt.");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        var rows = new List<string[]>();
        foreach (var row in document.Descendants().Where(e => e.Name.LocalName == "row"))
        {
            var cells = new Dictionary<int, string>();
            var max = -1;
            foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "c"))
            {
                var index = ColumnIndex((string?)cell.Attribute("r"));
                if (index < 0) continue;
                cells[index] = CellText(cell, shared);
                max = Math.Max(max, index);
            }

            rows.Add(Enumerable.Range(0, max + 1).Select(i => cells.GetValueOrDefault(i, string.Empty)).ToArray());
        }

        return new XlsxTable(rows);
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var stream = entry.Open();
        return XDocument.Load(stream).Descendants().Where(e => e.Name.LocalName == "si").Select(TextOf).ToList();
    }

    private static string CellText(XElement cell, List<string> shared)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr") return TextOf(cell);
        var value = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? string.Empty;
        return type == "s" && int.TryParse(value, out var i) && i >= 0 && i < shared.Count ? shared[i] : value;
    }

    // Alle Textteile eines Elements (auch formatierte Abschnitte), ohne Phonetik.
    private static string TextOf(XElement element) =>
        string.Concat(element.Descendants().Where(e => e.Name.LocalName == "t" && e.Parent?.Name.LocalName != "rPh").Select(e => e.Value));

    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return -1;
        var index = 0;
        foreach (var ch in reference.TakeWhile(char.IsLetter)) index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return index - 1;
    }
}

/// <summary>Eine eingelesene Tabelle: Kopfzeile plus Datenzeilen, Spalten werden über ihren Namen angesprochen (mehrfache Leerzeichen und Ränder spielen keine Rolle).</summary>
public sealed class XlsxTable
{
    private readonly Dictionary<string, int> _columns;

    public XlsxTable(IReadOnlyList<string[]> rows)
    {
        Header = rows.Count > 0 ? rows[0] : [];
        _columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Header.Length; i++) _columns.TryAdd(Normalize(Header[i]), i);
        Rows = rows.Skip(1).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).Select(r => new XlsxRow(this, r)).ToList();
    }

    public string[] Header { get; }
    public IReadOnlyList<XlsxRow> Rows { get; }

    public bool Has(string column) => _columns.ContainsKey(Normalize(column));

    internal int IndexOf(string column) => _columns.TryGetValue(Normalize(column), out var i) ? i : -1;

    private static string Normalize(string name) => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public sealed class XlsxRow(XlsxTable table, string[] cells)
{
    /// <summary>Der Wert der Spalte ohne Ränder; leer, wenn die Spalte oder der Wert fehlt.</summary>
    public string this[string column]
    {
        get
        {
            var i = table.IndexOf(column);
            return i >= 0 && i < cells.Length ? cells[i].Trim() : string.Empty;
        }
    }

    public string? Text(string column) => this[column] is { Length: > 0 } value ? value : null;
}
