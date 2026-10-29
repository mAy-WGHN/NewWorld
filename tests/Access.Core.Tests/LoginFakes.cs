using Access.Core.Login;
using Access.Core.People;

namespace Access.Core.Tests;

internal sealed class FakeDirectory(IEnumerable<Person> people) : IPersonDirectory
{
    private readonly List<Person> _people = [.. people];

    /// <summary>Whether the role for former employees holds a privilege (then they may still log in).</summary>
    public bool FormerEmployeesMayLogin { get; set; }

    public Task<bool> FormerEmployeesMayLoginAsync(CancellationToken cancellationToken) => Task.FromResult(FormerEmployeesMayLogin);

    public Task<Person?> FindByPersonnelNumberAsync(string personnelNumber, CancellationToken cancellationToken) =>
        Task.FromResult(_people.FirstOrDefault(p => p.PersonnelNumber == personnelNumber));

    public Task<Person?> FindAsync(Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult(_people.FirstOrDefault(p => p.EmployeeId == employeeId));
}

internal sealed class FakeBc : IBcLoginGateway
{
    public Dictionary<string, string> Pins { get; } = [];
    public bool Down { get; set; }
    public List<(string Number, string Pin)> Calls { get; } = [];

    public Task<BcPinResult> VerifyAsync(string personnelNumber, string pin, CancellationToken cancellationToken)
    {
        Calls.Add((personnelNumber, pin));
        return Down
            ? throw new BcLoginUnavailableException()
            : Task.FromResult(new BcPinResult(Pins.TryGetValue(personnelNumber, out var expected) && expected == pin));
    }
}

internal sealed class FakeCredentials : ICredentialStore
{
    public Dictionary<Guid, StoredCredential> Rows { get; } = [];

    public Task<StoredCredential?> GetAsync(Guid employeeId, CancellationToken cancellationToken) => Task.FromResult(Rows.GetValueOrDefault(employeeId));

    public Task SaveAsync(Guid employeeId, StoredCredential credential, CancellationToken cancellationToken)
    {
        Rows[employeeId] = credential;
        return Task.CompletedTask;
    }
}

internal sealed class FakeRefreshTokens : IRefreshTokenStore
{
    public List<RefreshTokenEntry> Rows { get; } = [];

    public Task AddAsync(RefreshTokenEntry entry, CancellationToken cancellationToken)
    {
        Rows.Add(entry);
        return Task.CompletedTask;
    }

    public Task<RefreshTokenEntry?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.TokenHash == tokenHash));

    public Task MarkUsedAsync(Guid id, CancellationToken cancellationToken)
    {
        Rows[Rows.FindIndex(r => r.Id == id)] = Rows.First(r => r.Id == id) with { Used = true };
        return Task.CompletedTask;
    }

    public Task RevokeAllAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        Rows.RemoveAll(r => r.EmployeeId == employeeId);
        return Task.CompletedTask;
    }
}
