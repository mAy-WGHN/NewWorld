using Workforce.Core.Absences;

namespace Workforce.Core.Unavailability;

/// <summary>Codes of the kinds the system itself uses: they come from the absences, never from a manual entry.</summary>
public static class UnavailabilityKinds
{
    public const string Sickness = "KRANK";
    public const string Vacation = "URLAUB";

    /// <summary>Sick during approved vacation: the vacation days are credited back, the days show as this kind.</summary>
    public const string SickInVacation = "KRANK I URLAUB";
}

/// <summary>
/// A reason why a person does not show up for work (BC list "Nichtverfügbarkeitsarten"). HR maintains the list; kinds are deactivated, never
/// deleted. The three system kinds stay active and are not entered by hand.
/// </summary>
public sealed class UnavailabilityKind
{
    public Guid Id { get; set; }

    /// <summary>Unique (case-insensitive), never changes.</summary>
    public required string Code { get; set; }

    public required string Description { get; set; }

    /// <summary>The time counts as paid (payroll needs it).</summary>
    public bool PaidTime { get; set; }

    /// <summary>The sign shown in the schedule afterwards (instead of "empty"); payroll needs it. <c>null</c> = none.</summary>
    public string? ScheduleSymbol { get; set; }

    /// <summary>BC column "Arbeitscode setzen" (empty in all kinds so far; kept, no effect yet).</summary>
    public string? WorkCode { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsSystem { get; set; }
}

public enum EntrySource
{
    Unknown = 0,

    /// <summary>Entered by HR.</summary>
    Manual = 1,

    /// <summary>Created by the system out of an absence (the KU days of a sick note).</summary>
    System = 2,
}

/// <summary>
/// A period of one kind for one person. Whole days (<see cref="DateOnly"/>), both ends included. All period logic lives in
/// <see cref="Range"/> (<see cref="DateRange"/>), so a later switch to times of day touches this type, not its users.
/// </summary>
public sealed class UnavailabilityEntry
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid KindId { get; set; }

    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public EntrySource Source { get; set; }

    /// <summary>System entries only: the sick note the days belong to.</summary>
    public Guid? SourceAbsenceId { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateRange Range => new(From, To);
}

public enum UnavailabilityError
{
    None = 0,
    UnknownPerson = 1,
    NotFound = 2,
    InvalidPeriod = 3,

    /// <summary>The period starts before the person joined.</summary>
    BeforeEntry = 4,

    AfterExit = 5,

    /// <summary>Another entry or an active absence covers a day of the period.</summary>
    Overlap = 6,

    UnknownKind = 7,

    /// <summary>The kind is deactivated.</summary>
    KindInactive = 8,

    /// <summary>Sickness, vacation and KU come from the absences.</summary>
    KindNotEnterable = 9,

    DuplicateCode = 10,

    /// <summary>Code or description missing or too long.</summary>
    InvalidKind = 11,

    /// <summary>A system kind cannot be deactivated.</summary>
    SystemKind = 12,

    /// <summary>The system created this entry; it follows its sick note and cannot be removed by hand.</summary>
    SystemEntry = 13,
}

public sealed record UnavailabilityResult(UnavailabilityError Error, UnavailabilityEntry? Entry = null);

public sealed record KindResult(UnavailabilityError Error, UnavailabilityKind? Kind = null);

/// <summary>The list of kinds HR starts with (the 24 of the BC list); the migration seeds exactly this.</summary>
public static class UnavailabilityKindSeed
{
    public sealed record Seed(string Code, string Description, bool PaidTime, string? ScheduleSymbol, bool IsSystem = false)
    {
        public Guid Id => DeterministicGuid.From("unavailability-kind:" + Code);

        public UnavailabilityKind ToKind() => new()
        {
            Id = Id, Code = Code, Description = Description, PaidTime = PaidTime, ScheduleSymbol = ScheduleSymbol, IsActive = true, IsSystem = IsSystem,
        };
    }

    public static readonly IReadOnlyList<Seed> All =
    [
        new("BESCHÄFTIGUNGSVERBOT", "Beschäftigungsverbot", false, "BV"),
        new("ELTERNZEIT", "Elternzeit", false, "EZ"),
        new("FORTBILDUNG", "Fortbildung", false, "FB"),
        new("FREI", "Freier Tag", false, "FT"),
        new("KINDKRANK", "krank Kind mit Bescheinig", true, "KK"),
        new("KLASSENFAHRT", "unbezahlt Info Klassenfahrt", false, null),
        new(UnavailabilityKinds.Sickness, "Krank", true, "K", IsSystem: true),
        new(UnavailabilityKinds.SickInVacation, "Krank im urlaub", true, "KU", IsSystem: true),
        new("MEHRARBEITSABBAU", "Abbau von Mehrarbeit", false, "MA"),
        new("MUTTERSCHUTZ", "Mutterschutz", false, "MS"),
        new("PFLEGE ANGEHÖRIGE", "Pflege od. Betreuung von Ang.", false, "PA"),
        new("PRÜFUNG", "Prüfung", true, "P"),
        new("PRÜFUNGSVORBEREITUNG", "PRÜFUNGSVORBEREITUNG", true, "PV"),
        new("QUARANTÄNE", "Quarantäne", false, "Q"),
        new("REHA", "Reha-Maßnahme", false, "Reh"),
        new("ROTATIONSFREI", "Feiertag Rotation", false, "RF"),
        new("SONDERURLAUB", "Sonder-Urlaub", false, "SU"),
        new("TOD", "Tod", true, "Tod"),
        new("UB", "unwiderr. bezahlt Freistellung", true, "UB"),
        new("UNBEZ. U", "Unbezahlter Urlaub", false, "UBE"),
        new("UNENTSCHULDIGT", "Unentschuldigt gefehlt", false, "UE"),
        new(UnavailabilityKinds.Vacation, "Urlaub", true, "U", IsSystem: true),
        new("URLAUBSANTRAGSZEITRAUM", "Urlaubsantrags Zeitraum", false, "UZ"),
        new("WIEDEREINGLIEDERUNG", "Wiedereingliederung", false, "WG"),
    ];
}
