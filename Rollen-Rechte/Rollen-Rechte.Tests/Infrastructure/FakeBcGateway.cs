using System.Collections.Concurrent;
using Rollen_Rechte.Services;

namespace Rollen_Rechte.Tests.Infrastructure;

/// <summary>Ersetzt die BC-Anbindung: Personalnummer/PIN und Filialen sind frei vorgebbar, kein Netzwerk.</summary>
public class FakeBcGateway : IBcGateway
{
    private readonly ConcurrentDictionary<string, (string Pin, string Name)> _employees = new();
    private readonly ConcurrentQueue<string> _statusCalls = new();

    public bool IsConfigured => true;

    /// <summary>true simuliert "BC-Anbindung oder BC nicht erreichbar" (technischer Fehler, nicht "nicht authentifiziert").</summary>
    public bool Unavailable { get; set; }

    public List<(string No, string Name)> Stores { get; } = [];

    /// <summary>Personalnummern, für die eine Prüfung angefragt wurde (das PIN wird bewusst nicht mitgeschrieben).</summary>
    public IReadOnlyCollection<string> StatusCalls => _statusCalls;

    public void AddEmployee(string employeeNo, string pin, string name = "Test Mitarbeiter") =>
        _employees[employeeNo] = (pin, name);

    public void SetPin(string employeeNo, string pin) =>
        _employees[employeeNo] = (pin, _employees[employeeNo].Name);

    public void Reset()
    {
        Unavailable = false;
        Stores.Clear();
        _employees.Clear();
        _statusCalls.Clear();
    }

    public Task<BcEmployeeCheck> VerifyEmployeeAsync(string employeeNo, string pin, CancellationToken ct)
    {
        if (Unavailable) throw new BcGatewayUnavailableException("BC nicht erreichbar.");
        _statusCalls.Enqueue(employeeNo);
        var ok = _employees.TryGetValue(employeeNo, out var e) && e.Pin == pin;
        return Task.FromResult(new BcEmployeeCheck(ok, ok ? e.Name : null));
    }

    public Task<IReadOnlyList<BcStore>> GetStoresAsync(CancellationToken ct)
    {
        if (Unavailable) throw new BcGatewayUnavailableException("BC nicht erreichbar.");
        return Task.FromResult<IReadOnlyList<BcStore>>(Stores.Select(s => new BcStore(s.No, s.Name)).ToList());
    }
}
