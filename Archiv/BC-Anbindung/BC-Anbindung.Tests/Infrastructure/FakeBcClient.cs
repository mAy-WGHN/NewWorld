using System.Collections.Concurrent;
using System.Text.Json;
using BC_Anbindung.BusinessCentral;

namespace BC_Anbindung.Tests.Infrastructure;

/// <summary>Ersetzt Business Central: Personalnummer/PIN und Filialen sind frei vorgebbar, kein Netzwerk.</summary>
public class FakeBcClient : IBcCodeunitClient
{
    private readonly ConcurrentDictionary<string, (string Pin, string Name)> _employees = new();
    private readonly ConcurrentQueue<string> _statusCalls = new();
    private int _storeCalls;

    /// <summary>true simuliert "BC nicht erreichbar" (technischer Fehler, nicht "success:false").</summary>
    public bool Unavailable { get; set; }

    /// <summary>Der Rohtext, den "GetStatus" liefern soll, statt der normalen Antwort (z. B. kaputtes JSON).</summary>
    public string? StatusOverride { get; set; }

    public List<(string No, string Name)> Stores { get; } = [];

    /// <summary>Personalnummern, für die GetStatus aufgerufen wurde (das PIN wird bewusst nicht mitgeschrieben).</summary>
    public IReadOnlyCollection<string> StatusCalls => _statusCalls;

    public int StoreCalls => _storeCalls;

    public void AddEmployee(string employeeNo, string pin, string name = "Test Mitarbeiter") =>
        _employees[employeeNo] = (pin, name);

    public void Reset()
    {
        Unavailable = false;
        StatusOverride = null;
        Stores.Clear();
        _employees.Clear();
        _statusCalls.Clear();
        _storeCalls = 0;
    }

    public Task<string> CallAsync(string codeunitName, string operationName,
        IEnumerable<(string Name, string Value)> parameters, CancellationToken ct)
    {
        if (Unavailable) throw new BcAuthException("BC nicht erreichbar.", "geheimer-soap-body-mit-pin");
        var args = parameters.ToDictionary(p => p.Name, p => p.Value);

        return (codeunitName, operationName) switch
        {
            ("Zeiterfassung", "GetStatus") => Task.FromResult(StatusOverride ?? GetStatus(args["employeeNo"], args["password"])),
            ("GetStores", "GetStores") => Task.FromResult(GetStores()),
            _ => throw new InvalidOperationException($"Unerwarteter BC-Aufruf {codeunitName}.{operationName}")
        };
    }

    private string GetStatus(string employeeNo, string pin)
    {
        _statusCalls.Enqueue(employeeNo);
        return _employees.TryGetValue(employeeNo, out var e) && e.Pin == pin
            ? JsonSerializer.Serialize(new { success = true, employeeName = e.Name })
            : JsonSerializer.Serialize(new { success = false, message = "Ungültig" });
    }

    private string GetStores()
    {
        Interlocked.Increment(ref _storeCalls);
        return JsonSerializer.Serialize(new
        {
            DynDataSet = new
            {
                DataSetName = "LSC Store",
                DataSetFields = new[] { new { FieldIndex = 0, FieldName = "No." }, new { FieldIndex = 1, FieldName = "Name" } },
                DataSetRows = Stores.Select(s => new
                {
                    Fields = new[] { new { FieldIndex = 0, FieldValue = s.No }, new { FieldIndex = 1, FieldValue = s.Name } }
                })
            }
        });
    }
}
