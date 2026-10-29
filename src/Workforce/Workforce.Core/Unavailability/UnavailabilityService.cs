using Workforce.Contracts;
using Workforce.Core.Absences;

namespace Workforce.Core.Unavailability;

/// <summary>
/// The kinds of unavailability (HR maintains them) and the entries of HR. Sickness and vacation stay with the absences and appear in the
/// timeline as KRANK and URLAUB. Callers check authorization first and save the unit of work afterwards. Business failures are returned
/// as <see cref="UnavailabilityError"/>; nothing is stored or published then. Messages carry no kind (class 2): only "unavailable".
/// TODO(BC-Modell): the mirror of entries to BC is not defined yet (no message, no sync status).
/// </summary>
public sealed class UnavailabilityService(
    IUnavailabilityStore store,
    IAbsenceStore absences,
    IPeopleDirectory people,
    IWorkforceMessages messages,
    ShiftCoverage coverage,
    TimeProvider time)
{
    private const int MaxCode = 40;
    private const int MaxDescription = 100;
    private const int MaxSymbol = 10;
    private const int MaxWorkCode = 20;

    // ------------------------------------------------------------------ kinds

    public async Task<IReadOnlyList<UnavailabilityKind>> KindsAsync(bool includeInactive, CancellationToken cancellationToken) =>
        [.. (await store.KindsAsync(cancellationToken)).Where(k => includeInactive || k.IsActive).OrderBy(k => k.Code, StringComparer.OrdinalIgnoreCase)];

    public async Task<KindResult> AddKindAsync(string code, string description, bool paidTime, string? scheduleSymbol, string? workCode, CancellationToken cancellationToken)
    {
        code = code?.Trim() ?? string.Empty;
        if (!IsValid(code, description, scheduleSymbol, workCode, MaxCode))
        {
            return new KindResult(UnavailabilityError.InvalidKind);
        }

        if (await store.KindByCodeAsync(code, cancellationToken) is not null)
        {
            return new KindResult(UnavailabilityError.DuplicateCode);
        }

        var kind = new UnavailabilityKind
        {
            Id = Guid.CreateVersion7(time.GetUtcNow()), Code = code, Description = description.Trim(), PaidTime = paidTime,
            ScheduleSymbol = Blank(scheduleSymbol), WorkCode = Blank(workCode), IsActive = true, IsSystem = false,
        };
        store.AddKind(kind);
        return new KindResult(UnavailabilityError.None, kind);
    }

    /// <summary>Changes everything but the code.</summary>
    public async Task<UnavailabilityError> UpdateKindAsync(Guid id, string description, bool paidTime, string? scheduleSymbol, string? workCode, CancellationToken cancellationToken)
    {
        if (await store.KindAsync(id, cancellationToken) is not { } kind)
        {
            return UnavailabilityError.UnknownKind;
        }

        if (!IsValid(kind.Code, description, scheduleSymbol, workCode, MaxCode))
        {
            return UnavailabilityError.InvalidKind;
        }

        kind.Description = description.Trim();
        kind.PaidTime = paidTime;
        kind.ScheduleSymbol = Blank(scheduleSymbol);
        kind.WorkCode = Blank(workCode);
        return UnavailabilityError.None;
    }

    /// <summary>Kinds are deactivated, never deleted; entries that use a deactivated kind stay.</summary>
    public async Task<UnavailabilityError> SetKindActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        if (await store.KindAsync(id, cancellationToken) is not { } kind)
        {
            return UnavailabilityError.UnknownKind;
        }

        if (kind.IsSystem && !active)
        {
            return UnavailabilityError.SystemKind;
        }

        kind.IsActive = active;
        return UnavailabilityError.None;
    }

    // ------------------------------------------------------------------ entries

    /// <summary>HR enters a period of a kind for a person.</summary>
    public async Task<UnavailabilityResult> AddEntryAsync(Guid employeeId, Guid kindId, DateOnly from, DateOnly to, Guid actorId, CancellationToken cancellationToken)
    {
        if (await people.GetAsync(employeeId, cancellationToken) is not { } person)
        {
            return new UnavailabilityResult(UnavailabilityError.UnknownPerson);
        }

        if (await store.KindAsync(kindId, cancellationToken) is not { } kind)
        {
            return new UnavailabilityResult(UnavailabilityError.UnknownKind);
        }

        if (!kind.IsActive)
        {
            return new UnavailabilityResult(UnavailabilityError.KindInactive);
        }

        if (kind.IsSystem)
        {
            return new UnavailabilityResult(UnavailabilityError.KindNotEnterable);
        }

        if (to < from)
        {
            return new UnavailabilityResult(UnavailabilityError.InvalidPeriod);
        }

        if (person.EntryDate is { } entry && from < entry)
        {
            return new UnavailabilityResult(UnavailabilityError.BeforeEntry);
        }

        if (person.ExitDate is { } exit && to > exit)
        {
            return new UnavailabilityResult(UnavailabilityError.AfterExit);
        }

        await absences.LockEmployeeAsync(employeeId, cancellationToken);
        var range = new DateRange(from, to);
        if ((await store.EntriesAsync(employeeId, cancellationToken)).Any(e => e.Range.Overlaps(range))
            || (await absences.ForEmployeeAsync(employeeId, cancellationToken)).Any(a => a.IsActive && a.Range.Overlaps(range)))
        {
            return new UnavailabilityResult(UnavailabilityError.Overlap);
        }

        var now = time.GetUtcNow();
        var correlationId = Guid.CreateVersion7(now);
        var created = new UnavailabilityEntry
        {
            Id = Guid.CreateVersion7(now), EmployeeId = employeeId, KindId = kindId, From = from, To = to, Source = EntrySource.Manual, CreatedBy = actorId, CreatedAt = now,
        };
        store.AddEntry(created);
        Availability(employeeId, from, to, AvailabilityStatus.Unavailable, correlationId, now);
        await coverage.ReportAsync(employeeId, range, UncoveredReason.Absence, created.Id, correlationId, cancellationToken);
        return new UnavailabilityResult(UnavailabilityError.None, created);
    }

    /// <summary>HR removes an entry it made. Entries the system created follow their sick note and stay.</summary>
    public async Task<UnavailabilityError> RemoveEntryAsync(Guid employeeId, Guid entryId, CancellationToken cancellationToken)
    {
        if (await store.EntryAsync(entryId, cancellationToken) is not { } entry || entry.EmployeeId != employeeId)
        {
            return UnavailabilityError.NotFound;
        }

        if (entry.Source != EntrySource.Manual)
        {
            return UnavailabilityError.SystemEntry;
        }

        await absences.LockEmployeeAsync(employeeId, cancellationToken);
        var now = time.GetUtcNow();
        store.RemoveEntry(entry);
        Availability(employeeId, entry.From, entry.To, AvailabilityStatus.Available, Guid.CreateVersion7(now), now);
        return UnavailabilityError.None;
    }

    /// <summary>Everything why the person is not available, as one list (the caller decides what the viewer may see of it).</summary>
    public async Task<IReadOnlyList<UnavailabilityItem>> TimelineAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var items = UnavailabilityTimeline.Build(
            employeeId, await absences.ForEmployeeAsync(employeeId, cancellationToken), await store.EntriesAsync(employeeId, cancellationToken), await store.KindsAsync(cancellationToken));
        if (await people.GetAsync(employeeId, cancellationToken) is not { ExitDate: { } exit })
        {
            return items;
        }

        // Periods that reach past the exit date are only marked (HR sees a hint); nothing is cut or removed.
        return [.. items.Select(i => i with { AfterExit = i.To is null || i.To > exit })];
    }

    // ------------------------------------------------------------------ helpers

    private static bool IsValid(string code, string? description, string? symbol, string? workCode, int maxCode) =>
        code.Length is > 0 && code.Length <= maxCode
        && description?.Trim() is { Length: > 0 } d && d.Length <= MaxDescription
        && (Blank(symbol) is not { } s || s.Length <= MaxSymbol)
        && (Blank(workCode) is not { } w || w.Length <= MaxWorkCode);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Neutralised: no kind, no reason.</summary>
    private void Availability(Guid employeeId, DateOnly from, DateOnly to, AvailabilityStatus status, Guid correlationId, DateTimeOffset now) =>
        messages.Publish(new EmployeeAvailabilityChanged(Guid.CreateVersion7(now), correlationId, now, employeeId, from, to, status));
}
