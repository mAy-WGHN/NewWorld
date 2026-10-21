using System.Collections.Concurrent;
using Benachrichtigungen.Services;

namespace Benachrichtigungen.Tests.Infrastructure;

public record DirectoryCall(string Privilege, IReadOnlyCollection<Guid> LocationIds, IReadOnlyCollection<Guid> ObjectIds, IReadOnlyCollection<Guid> Candidates);

/// <summary>
/// Ersetzt "Rollen-Rechte": Ein Test legt fest, welche User den Vorgang sehen dürfen (<see cref="Allowed"/>) — oder dass der Dienst nicht erreichbar ist.
/// Antwort = die Kandidaten, die erlaubt sind (wie beim echten Dienst, der nur Kandidaten zurückgibt).
/// </summary>
public class FakeRecipientDirectory : IRecipientDirectory
{
    private readonly ConcurrentQueue<DirectoryCall> _calls = new();

    public HashSet<Guid> Allowed { get; } = [];

    /// <summary>true: "Rollen-Rechte" ist nicht erreichbar.</summary>
    public bool Unavailable { get; set; }

    /// <summary>true: Jeder Kandidat darf (ohne dass der Test ihn einzeln freigeben muss).</summary>
    public bool AllowEveryone { get; set; }

    public IReadOnlyCollection<DirectoryCall> Calls => _calls;

    public Task<IReadOnlyCollection<Guid>> FindUsersAsync(string privilege, IReadOnlyCollection<Guid> locationIds, IReadOnlyCollection<Guid> objectIds,
        IReadOnlyCollection<Guid> candidateUserIds, CancellationToken ct)
    {
        _calls.Enqueue(new DirectoryCall(privilege, locationIds.ToList(), objectIds.ToList(), candidateUserIds.ToList()));
        if (Unavailable) throw new RecipientDirectoryUnavailableException("nicht erreichbar");
        return Task.FromResult<IReadOnlyCollection<Guid>>(candidateUserIds.Where(id => AllowEveryone || Allowed.Contains(id)).ToList());
    }

    public void Reset()
    {
        Allowed.Clear();
        Unavailable = false;
        AllowEveryone = false;
        _calls.Clear();
    }
}
