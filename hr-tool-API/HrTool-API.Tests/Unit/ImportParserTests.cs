using HrTool_API.Services.Import;
using HrTool_API.Tests.Infrastructure;

namespace HrTool_API.Tests.Unit;

/// <summary>Die Schreibweisen der BC-Exporte (Beschäftigungscodes, Monatsnamen, Status) und das Einlesen von .xlsx-Dateien.</summary>
public class ImportParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hr-import-tests", Guid.NewGuid().ToString("N"));

    public ImportParserTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // Die Schreibweisen stammen aus dem Export „Mitarbeiterliste (Personalplanung)“ (81 verschiedene Codes).
    [Theory]
    [InlineData("TZ 15,25H", 15.25)]
    [InlineData("TZ 15,50H", 15.5)]
    [InlineData("MINI 9,00H", 9)]
    [InlineData("MINI 3,5H", 3.5)]
    [InlineData("AZU 40H", 40)]
    [InlineData("TZ 21,25", 21.25)]       // ohne „H“
    [InlineData("TZ 25,50 H", 25.5)]      // mit Leerzeichen vor dem „H“
    [InlineData("TZ 21H", 21)]
    [InlineData("VZ 40H", 40)]
    public void WeeklyHours_AreReadFromTheContractCode(string code, double expected)
    {
        Assert.Equal((decimal)expected, ImportParsers.ParseWeeklyHours(code));
    }

    [Theory]
    [InlineData("VERKAUF")]
    [InlineData("")]
    public void WeeklyHours_AreNullWithoutANumber(string code) => Assert.Null(ImportParsers.ParseWeeklyHours(code));

    [Theory]
    [InlineData("April  2021", 2021, 4)]      // zwei Leerzeichen wie im Export
    [InlineData("März 2026", 2026, 3)]
    [InlineData("Dezember 2025", 2025, 12)]
    [InlineData("januar 2030", 2030, 1)]
    public void MonthNames_AreReadAsYearAndMonth(string text, int year, int month)
    {
        Assert.Equal((year, month), ImportParsers.ParseMonthName(text));
    }

    [Theory]
    [InlineData("Quartal 2026")]
    [InlineData("April")]
    [InlineData("2026")]
    [InlineData("")]
    public void MonthNames_AreNullForAnythingElse(string text) => Assert.Null(ImportParsers.ParseMonthName(text));

    [Fact]
    public void StatusAndKind_KnowTheExportValues_AndNothingElse()
    {
        Assert.Equal(ScheduleStatus.Draft, ImportParsers.ParseScheduleStatus("Entwurf"));
        Assert.Equal(ScheduleStatus.Confirmed, ImportParsers.ParseScheduleStatus("Bestätigt"));
        Assert.Null(ImportParsers.ParseScheduleStatus("Offen"));
        Assert.Equal(ShiftPatternKind.Day, ImportParsers.ParseShiftPatternKind("Tagesmuster"));
        Assert.Equal(ShiftPatternKind.Week, ImportParsers.ParseShiftPatternKind("Wochenmuster"));
        Assert.Null(ImportParsers.ParseShiftPatternKind("Monatsmuster"));
    }

    [Theory]
    [InlineData("1023.5", 1023.5)]
    [InlineData("1018", 1018)]
    [InlineData("12,5", 12.5)]
    [InlineData("", 0)]
    [InlineData("kaputt", 0)]
    public void Numbers_UseThePointOfTheExports(string text, double expected) => Assert.Equal((decimal)expected, ImportParsers.ParseNumber(text));

    // --- .xlsx lesen --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]   // eingebettete Zeichenketten mit x:-Präfix (BC)
    [InlineData(true)]    // gemeinsame Zeichenketten und echte Zahlen (Excel)
    public void TheReader_ReadsBothWritingStyles_AndAddressesColumnsByName(bool shared)
    {
        var path = Path.Combine(_dir, "t.xlsx");
        XlsxBuilder.Write(path, ["Nr.", "Total Budget  (Std.)", "Name"], [["00001", "1023.5", "Ärger & Öl"], ["00002", "", "<tag>"], [], ["", "", ""]], shared);

        var table = XlsxReader.ReadFirstSheet(path);

        Assert.Equal(2, table.Rows.Count);                                          // die leeren Zeilen fallen weg
        Assert.Equal("00001", table.Rows[0]["Nr."]);                                // führende Nullen bleiben
        Assert.Equal("1023.5", table.Rows[0]["Total Budget (Std.)"]);               // doppeltes Leerzeichen im Kopf egal
        Assert.Equal("Ärger & Öl", table.Rows[0]["Name"]);
        Assert.Equal("", table.Rows[1]["Total Budget (Std.)"]);
        Assert.Null(table.Rows[1].Text("Total Budget (Std.)"));
        Assert.Equal("<tag>", table.Rows[1]["name"]);                               // Spaltennamen ohne Beachtung der Schreibweise
        Assert.Equal("", table.Rows[0]["Gibt es nicht"]);
        Assert.True(table.Has("Nr."));
        Assert.False(table.Has("Gibt es nicht"));
    }

    [Fact]
    public void TheReader_HandlesMoreThan26Columns()
    {
        var path = Path.Combine(_dir, "wide.xlsx");
        var header = Enumerable.Range(1, 30).Select(i => $"C{i}").ToArray();
        XlsxBuilder.Write(path, header, [Enumerable.Range(1, 30).Select(i => $"v{i}").ToArray()]);

        var row = XlsxReader.ReadFirstSheet(path).Rows.Single();

        Assert.Equal("v1", row["C1"]);
        Assert.Equal("v27", row["C27"]);   // Spalte AA
        Assert.Equal("v30", row["C30"]);
    }

    [Fact]
    public void ARealBcExport_CanBeRead_WhenItIsThere()
    {
        // Prüft die echten Exporte gegen den Reader, ohne ihre Inhalte zu prüfen (sie enthalten Personaldaten und liegen nicht im Repo).
        var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "spreadsheets"));
        var file = Path.Combine(folder, "Arbeitsorte.xlsx");
        if (!File.Exists(file)) return;

        var table = XlsxReader.ReadFirstSheet(file);

        Assert.Equal(42, table.Rows.Count);
        Assert.True(table.Has("Store Dim Code"));
        Assert.Equal("0012", table.Rows.Single(r => r["Code"] == "AH")["Store Dim Code"]);
    }
}
