using System.IO.Compression;
using System.Security;
using System.Text;

namespace HrTool_API.Tests.Infrastructure;

/// <summary>
/// Schreibt kleine .xlsx-Dateien für Tests. Zwei Schreibweisen wie in den BC-Exporten bzw. in Excel:
/// eingebettete Zeichenketten mit Präfix <c>x:</c> (BC) oder gemeinsame Zeichenketten und echte Zahlen (<c>shared = true</c>).
/// </summary>
public static class XlsxBuilder
{
    public static void Write(string path, string[] header, IEnumerable<string[]> rows, bool shared = false)
    {
        var all = new[] { header }.Concat(rows).ToList();
        var strings = new List<string>();
        var sheet = new StringBuilder();
        var prefix = shared ? "" : "x:";
        var ns = shared ? "xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"" : "xmlns:x=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"";
        sheet.Append($"<?xml version=\"1.0\" encoding=\"utf-8\"?><{prefix}worksheet {ns}><{prefix}sheetData>");
        for (var r = 0; r < all.Count; r++)
        {
            sheet.Append($"<{prefix}row r=\"{r + 1}\">");
            for (var c = 0; c < all[r].Length; c++)
            {
                var value = all[r][c];
                var reference = ColumnName(c) + (r + 1);
                if (value.Length == 0) continue;                                              // leere Zellen fehlen in Excel-Dateien
                if (shared && r > 0 && double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out _))
                    sheet.Append($"<c r=\"{reference}\"><v>{value}</v></c>");                   // echte Zahl
                else if (shared)
                {
                    strings.Add(value);
                    sheet.Append($"<c r=\"{reference}\" t=\"s\"><v>{strings.Count - 1}</v></c>");
                }
                else
                    sheet.Append($"<x:c r=\"{reference}\" t=\"inlineStr\"><x:is><x:t xml:space=\"preserve\">{SecurityElement.Escape(value)}</x:t></x:is></x:c>");
            }

            sheet.Append($"</{prefix}row>");
        }

        sheet.Append($"</{prefix}sheetData></{prefix}worksheet>");

        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
        Add(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"/>");
        Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        if (shared)
            Add(zip, "xl/sharedStrings.xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                                              + string.Concat(strings.Select(s => $"<si><t xml:space=\"preserve\">{SecurityElement.Escape(s)}</t></si>")) + "</sst>");
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ColumnName(int index)
    {
        var name = "";
        for (var i = index + 1; i > 0; i = (i - 1) / 26) name = (char)('A' + (i - 1) % 26) + name;
        return name;
    }
}
