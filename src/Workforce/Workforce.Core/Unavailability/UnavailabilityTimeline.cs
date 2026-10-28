using Workforce.Core.Absences;

namespace Workforce.Core.Unavailability;

public enum UnavailabilityOrigin
{
    Unknown = 0,

    /// <summary>Sickness or approved vacation (the absences).</summary>
    Absence = 1,

    /// <summary>Entered by HR.</summary>
    Manual = 2,

    /// <summary>KU days created by the system.</summary>
    System = 3,
}

/// <summary>One period of one kind in the unavailability of a person. Whole days; <see cref="To"/> = <c>null</c> is open (sickness until further notice).</summary>
public sealed record UnavailabilityItem(
    Guid Id, Guid EmployeeId, DateOnly From, DateOnly? To, string KindCode, string KindDescription, bool PaidTime, string? ScheduleSymbol, UnavailabilityOrigin Origin);

/// <summary>A period in which a person is not available, without any reason.</summary>
public sealed record AvailabilityPeriod(DateOnly From, DateOnly? To);

/// <summary>What colleagues in the same schedule see: sickness, vacation and KU by name (<see cref="KindCode"/> set), everything else only as "not available" (all kind fields <c>null</c>).</summary>
public sealed record ColleagueItem(DateOnly From, DateOnly? To, string? KindCode, string? KindDescription, string? ScheduleSymbol);

/// <summary>
/// Everything why a person is not available, as one list: sickness (KRANK) and approved vacation (URLAUB) from the absences, the entries of HR,
/// and the sick days inside approved vacation as KU. KU days are taken out of the sickness, so every day appears once. Pure; the reading of
/// the data and the check who may see the kind (see the endpoints) are done elsewhere.
/// </summary>
public static class UnavailabilityTimeline
{
    public static IReadOnlyList<UnavailabilityItem> Build(
        Guid employeeId, IEnumerable<Absence> absences, IEnumerable<UnavailabilityEntry> entries, IReadOnlyList<UnavailabilityKind> kinds)
    {
        var activeAbsences = absences.Where(a => a.EmployeeId == employeeId).ToList();
        var items = new List<UnavailabilityItem>();
        var entryList = entries.Where(e => e.EmployeeId == employeeId).ToList();

        foreach (var absence in activeAbsences)
        {
            if (absence is { Type: Contracts.AbsenceType.Sickness, Status: Contracts.AbsenceStatus.Reported })
            {
                var kind = KindOf(kinds, UnavailabilityKinds.Sickness, "Krank", "K");
                var cuts = entryList
                    .Where(e => e.Source == EntrySource.System && e.SourceAbsenceId == absence.Id)
                    .Select(e => Clip(e.Range, absence.Range)).OfType<DateRange>().ToList();
                foreach (var (from, to) in Subtract(absence.Range, cuts))
                {
                    items.Add(Item(absence.Id, employeeId, from, to, kind, UnavailabilityOrigin.Absence));
                }
            }
            else if (absence is { Type: Contracts.AbsenceType.Vacation, Status: Contracts.AbsenceStatus.Approved, To: { } to })
            {
                items.Add(Item(absence.Id, employeeId, absence.From, to, KindOf(kinds, UnavailabilityKinds.Vacation, "Urlaub", "U"), UnavailabilityOrigin.Absence));
            }
        }

        var byId = kinds.ToDictionary(k => k.Id);
        foreach (var entry in entryList)
        {
            if (!byId.TryGetValue(entry.KindId, out var kind))
            {
                continue;
            }

            if (entry.Source == EntrySource.System)
            {
                // The days follow their sick note: gone with it, cut to it when it was shortened.
                if (activeAbsences.FirstOrDefault(a => a.Id == entry.SourceAbsenceId && a.Status == Contracts.AbsenceStatus.Reported) is not { } sick
                    || Clip(entry.Range, sick.Range) is not { } clipped)
                {
                    continue;
                }

                items.Add(Item(entry.Id, employeeId, clipped.From, clipped.To, kind, UnavailabilityOrigin.System));
            }
            else
            {
                items.Add(Item(entry.Id, employeeId, entry.From, entry.To, kind, UnavailabilityOrigin.Manual));
            }
        }

        return [.. items.OrderBy(i => i.From).ThenBy(i => i.KindCode, StringComparer.Ordinal).ThenBy(i => i.Id)];
    }

    /// <summary>
    /// What a viewer without the right to see the kind gets: the days the person is not available, touching and overlapping periods merged,
    /// so that no kind can be guessed from where a period splits (e.g. KU inside a sick note).
    /// </summary>
    public static IReadOnlyList<AvailabilityPeriod> Neutralise(IEnumerable<UnavailabilityItem> items)
    {
        var result = new List<AvailabilityPeriod>();
        foreach (var item in items.OrderBy(i => i.From))
        {
            if (result.Count > 0 && (result[^1].To is not { } last || item.From <= last.AddDays(1)))
            {
                var current = result[^1];
                result[^1] = current with { To = current.To is null || item.To is null ? null : (item.To > current.To ? item.To : current.To) };
            }
            else
            {
                result.Add(new AvailabilityPeriod(item.From, item.To));
            }
        }

        return result;
    }

    /// <summary>
    /// The view for colleagues who see the same schedule (the availability privilege): what the schedule shows anyway, i.e. sickness, vacation and KU,
    /// by kind; every other kind only as "not available", with touching and overlapping periods of those merged (no kind guessable from a split).
    /// </summary>
    public static IReadOnlyList<ColleagueItem> ForColleagues(IEnumerable<UnavailabilityItem> items)
    {
        var list = items.ToList();
        var shown = list.Where(IsShownToColleagues).Select(i => new ColleagueItem(i.From, i.To, i.KindCode, i.KindDescription, i.ScheduleSymbol));
        var neutral = Neutralise(list.Where(i => !IsShownToColleagues(i))).Select(p => new ColleagueItem(p.From, p.To, null, null, null));
        return [.. shown.Concat(neutral).OrderBy(i => i.From).ThenBy(i => i.KindCode, StringComparer.Ordinal)];
    }

    private static bool IsShownToColleagues(UnavailabilityItem item) =>
        item.KindCode is UnavailabilityKinds.Sickness or UnavailabilityKinds.Vacation or UnavailabilityKinds.SickInVacation;

    private static UnavailabilityItem Item(Guid id, Guid employeeId, DateOnly from, DateOnly? to, UnavailabilityKind kind, UnavailabilityOrigin origin) =>
        new(id, employeeId, from, to, kind.Code, kind.Description, kind.PaidTime, kind.ScheduleSymbol, origin);

    private static UnavailabilityKind KindOf(IReadOnlyList<UnavailabilityKind> kinds, string code, string description, string symbol) =>
        kinds.FirstOrDefault(k => string.Equals(k.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? new UnavailabilityKind { Code = code, Description = description, ScheduleSymbol = symbol, PaidTime = true, IsSystem = true };

    /// <summary>The part of <paramref name="range"/> inside <paramref name="limit"/>, or <c>null</c> if nothing is left.</summary>
    private static DateRange? Clip(DateRange range, DateRange limit)
    {
        var from = range.From > limit.From ? range.From : limit.From;
        var to = (range.To, limit.To) switch
        {
            ({ } a, { } b) => a < b ? a : b,
            ({ } a, null) => a,
            (null, { } b) => b,
            _ => (DateOnly?)null,
        };
        return to is { } end && end < from ? null : new DateRange(from, to);
    }

    /// <summary>The days of <paramref name="whole"/> that are not covered by one of the (closed) <paramref name="cuts"/>.</summary>
    private static IEnumerable<(DateOnly From, DateOnly? To)> Subtract(DateRange whole, IReadOnlyList<DateRange> cuts)
    {
        var cursor = whole.From;
        foreach (var cut in cuts.OrderBy(c => c.From))
        {
            if (cut.From > cursor)
            {
                yield return (cursor, cut.From.AddDays(-1));
            }

            var next = cut.To!.Value.AddDays(1);
            if (next > cursor)
            {
                cursor = next;
            }
        }

        if (whole.To is null || cursor <= whole.To)
        {
            yield return (cursor, whole.To);
        }
    }
}
