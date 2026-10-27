using System.Globalization;
using Workforce.Core.Absences;

namespace Workforce.Core.Employees;

/// <summary>
/// Creating and changing the employee card, the number series of the personnel numbers and the selection lists, free of infrastructure.
/// Callers check authorization first and save the unit of work afterwards. The personnel number is assigned here from the number series
/// (continuing the existing one; HR enters the last number used), never entered. Business failures are returned as
/// <see cref="EmployeeError"/>; nothing is stored then (the caller does not commit).
/// </summary>
public sealed class EmployeeService(
    IEmployeeCardStore cards,
    IReferenceLists lists,
    IPersonnelNumberSeries series,
    IEntitlementStore entitlements,
    TimeProvider time,
    WorkforceOptions options)
{
    private const int MaxName = 100;

    private DateOnly TodayLocal => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), options.Zone).DateTime);

    // ------------------------------------------------------------------ card

    public async Task<EmployeeCard?> GetAsync(Guid employeeId, CancellationToken cancellationToken) => await cards.GetAsync(employeeId, cancellationToken);

    /// <summary>
    /// Creates the card of a new employee with the next personnel number and, if given, the yearly vacation entitlement for the year of hiring.
    /// </summary>
    public async Task<(EmployeeError Error, EmployeeCard? Card)> CreateAsync(
        EmployeeData data, decimal? vacationDaysPerYear, Guid actorId, CancellationToken cancellationToken)
    {
        if (!IsValid(data) || vacationDaysPerYear is < 0 or > 366)
        {
            return (EmployeeError.Invalid, null);
        }

        if (await CheckReferencesAsync(data, current: null, cancellationToken) is var referenceError and not EmployeeError.None)
        {
            return (referenceError, null);
        }

        if (await series.GetAsync(cancellationToken) is null)
        {
            return (EmployeeError.NumberSeriesNotSet, null);
        }

        var now = time.GetUtcNow();
        if (await series.NextAsync(now, cancellationToken) is not { } next)
        {
            return (EmployeeError.NumberSeriesExhausted, null);
        }

        var number = next.ToString(CultureInfo.InvariantCulture);
        if (await cards.PersonnelNumberExistsAsync(number, cancellationToken))
        {
            return (EmployeeError.DuplicateNumber, null);
        }

        var card = new EmployeeCard
        {
            EmployeeId = Guid.CreateVersion7(now),
            PersonnelNumber = number,
            FirstName = data.FirstName.Trim(),
            LastName = data.LastName.Trim(),
            ReceiptName = data.LastName.Trim(),
            CreatedAt = now,
            CreatedBy = actorId,
            ModifiedAt = now,
            State = CardState.PendingBc,
        };
        await Apply(card, data, cancellationToken);
        cards.Add(card);
        if (vacationDaysPerYear is { } days)
        {
            await entitlements.SetAsync(card.EmployeeId, card.HireDate.Year, days, cancellationToken);
        }

        return (EmployeeError.None, card);
    }

    /// <summary>
    /// Replaces the fields of a card (the personnel number never changes). The wage type can only be changed with
    /// <paramref name="mayChangeWageType"/>; everything else may change without it as long as the wage type stays the same.
    /// </summary>
    public async Task<EmployeeError> UpdateAsync(Guid employeeId, EmployeeData data, bool mayChangeWageType, Guid actorId, CancellationToken cancellationToken)
    {
        if (await cards.GetAsync(employeeId, cancellationToken) is not { } card)
        {
            return EmployeeError.NotFound;
        }

        if (!IsValid(data))
        {
            return EmployeeError.Invalid;
        }

        if (data.WageTypeId != card.WageTypeId && !mayChangeWageType)
        {
            return EmployeeError.NotAllowed;
        }

        if (await CheckReferencesAsync(data, current: card, cancellationToken) is var referenceError and not EmployeeError.None)
        {
            return referenceError;
        }

        await Apply(card, data, cancellationToken);
        card.ModifiedAt = time.GetUtcNow();
        await cards.CardChangedAsync(card, cancellationToken);
        return EmployeeError.None;
    }

    private async Task Apply(EmployeeCard card, EmployeeData data, CancellationToken cancellationToken)
    {
        card.FirstName = data.FirstName.Trim();
        card.LastName = data.LastName.Trim();
        card.ReceiptName = string.IsNullOrWhiteSpace(data.ReceiptName) ? card.LastName : data.ReceiptName.Trim();
        card.Gender = data.Gender;
        card.BirthDate = data.BirthDate;
        card.Street = Clean(data.Street);
        card.PostalCode = Clean(data.PostalCode);
        card.City = Clean(data.City);
        card.PrivatePhone = Clean(data.PrivatePhone);
        card.PrivateEmail = Clean(data.PrivateEmail);
        card.BusinessEmail = Clean(data.BusinessEmail);
        card.MainStoreId = data.MainStoreId;
        card.FunctionId = data.FunctionId;
        card.RoleId = data.RoleId;
        card.RegionId = data.RegionId;
        card.WorkplaceId = data.WorkplaceId;
        card.EmploymentRelationshipId = data.EmploymentRelationshipId;
        card.EmploymentCodeId = data.EmploymentCodeId;
        card.AuthorizationGroupId = data.AuthorizationGroupId;
        card.WageTypeId = data.WageTypeId;
        card.HireDate = data.HireDate;
        card.ExitDate = data.ExitDate;
        card.SupervisorId = Clean(data.SupervisorPersonnelNumber) is { } supervisor ? await cards.FindByPersonnelNumberAsync(supervisor, cancellationToken) : null;
        card.QtradoExport = data.QtradoExport;
        card.WebTimeTrackingAllowed = data.WebTimeTrackingAllowed;
        card.PasswordChangeRequired = data.PasswordChangeRequired;
        card.Note = Clean(data.Note);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool IsValid(EmployeeData d)
    {
        var today = TodayLocal;
        return Name(d.FirstName) && Name(d.LastName)
            && (string.IsNullOrWhiteSpace(d.ReceiptName) || d.ReceiptName.Trim().Length <= MaxName)
            && d.Gender is Gender.Female or Gender.Male or Gender.Diverse
            && d.HireDate.Year is >= 2000 and <= 2200
            && (d.ExitDate is not { } exit || exit >= d.HireDate)
            && (d.BirthDate is not { } birth || (birth.Year >= 1900 && birth < today && birth < d.HireDate))
            && Length(d.Street, 200) && Length(d.PostalCode, 10) && Length(d.City, MaxName) && Length(d.Note, 1000)
            && (Clean(d.PrivateEmail) is not { } privateEmail || EmployeeValidation.IsEmail(privateEmail))
            && (Clean(d.BusinessEmail) is not { } businessEmail || EmployeeValidation.IsEmail(businessEmail))
            && (Clean(d.PrivatePhone) is not { } phone || EmployeeValidation.IsPhone(phone));
    }

    private static bool Name(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaxName;

    private static bool Length(string? value, int max) => value is null || value.Trim().Length <= max;

    /// <summary>Selections must exist, belong to their list and be active (unless the card already has them), the store and the supervisor must exist.</summary>
    private async Task<EmployeeError> CheckReferencesAsync(EmployeeData d, EmployeeCard? current, CancellationToken cancellationToken)
    {
        foreach (var (id, kind, existing) in new (Guid?, ReferenceKind, Guid?)[]
        {
            (d.FunctionId, ReferenceKind.Function, current?.FunctionId),
            (d.RoleId, ReferenceKind.Role, current?.RoleId),
            (d.RegionId, ReferenceKind.Region, current?.RegionId),
            (d.WorkplaceId, ReferenceKind.Workplace, current?.WorkplaceId),
            (d.EmploymentRelationshipId, ReferenceKind.EmploymentRelationship, current?.EmploymentRelationshipId),
            (d.EmploymentCodeId, ReferenceKind.EmploymentCode, current?.EmploymentCodeId),
            (d.AuthorizationGroupId, ReferenceKind.AuthorizationGroup, current?.AuthorizationGroupId),
            (d.WageTypeId, ReferenceKind.WageType, current?.WageTypeId),
        })
        {
            if (id is not { } entryId)
            {
                continue;
            }

            if (await lists.GetAsync(entryId, cancellationToken) is not { } entry || entry.Kind != kind || (!entry.IsActive && existing != entryId))
            {
                return EmployeeError.UnknownReference;
            }
        }

        if (d.MainStoreId is { } store && store != current?.MainStoreId && !await cards.StoreIsActiveAsync(store, cancellationToken))
        {
            return EmployeeError.UnknownStore;
        }

        if (Clean(d.SupervisorPersonnelNumber) is { } supervisor && await cards.FindByPersonnelNumberAsync(supervisor, cancellationToken) is null)
        {
            return EmployeeError.UnknownSupervisor;
        }

        return EmployeeError.None;
    }

    // ------------------------------------------------------------------ number series

    /// <summary>The default range of the personnel numbers (the existing number series).</summary>
    public const long DefaultStart = 1001;

    public const long DefaultEnd = 99999;

    /// <summary>A personnel number has at most nine digits.</summary>
    private const long HighestNumber = 999_999_999;

    public async Task<NumberSeriesState?> GetNumberSeriesAsync(CancellationToken cancellationToken) => await series.GetAsync(cancellationToken);

    /// <summary>
    /// HR enters the last personnel number used so far (or corrects it) and, if needed, the range. Without a range the existing one stays
    /// (at the first entry 1001 to 99999). The last number lies inside the range or one below its start.
    /// </summary>
    public async Task<EmployeeError> SetNumberSeriesAsync(long last, long? start, long? end, Guid actorId, CancellationToken cancellationToken)
    {
        var existing = await series.GetAsync(cancellationToken);
        var from = start ?? existing?.Start ?? DefaultStart;
        var to = end ?? existing?.End ?? DefaultEnd;
        if (from < 1 || to < from || to > HighestNumber || last < from - 1 || last > to)
        {
            return EmployeeError.Invalid;
        }

        await series.SetAsync(new NumberSeriesState(from, to, last), actorId, time.GetUtcNow(), cancellationToken);
        return EmployeeError.None;
    }

    // ------------------------------------------------------------------ selection lists

    public async Task<IReadOnlyList<ReferenceEntry>> ListAsync(ReferenceKind kind, CancellationToken cancellationToken) =>
        await lists.ListAsync(kind, cancellationToken);

    public async Task<(EmployeeError Error, ReferenceEntry? Entry)> AddEntryAsync(ReferenceKind kind, string code, string name, CancellationToken cancellationToken)
    {
        if (kind == ReferenceKind.Unknown || !Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(code) || code.Trim().Length > 30 || !Name(name))
        {
            return (EmployeeError.Invalid, null);
        }

        if ((await lists.ListAsync(kind, cancellationToken)).Any(e => string.Equals(e.Code, code.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return (EmployeeError.DuplicateCode, null);
        }

        var entry = new ReferenceEntry { Id = Guid.CreateVersion7(time.GetUtcNow()), Kind = kind, Code = code.Trim(), Name = name.Trim(), IsActive = true };
        lists.Add(entry);
        return (EmployeeError.None, entry);
    }

    /// <summary>Renames or (de)activates an entry. Entries are never deleted; deactivated ones cannot be chosen any more but stay on cards that have them.</summary>
    public async Task<EmployeeError> UpdateEntryAsync(Guid id, string name, bool isActive, CancellationToken cancellationToken)
    {
        if (await lists.GetAsync(id, cancellationToken) is not { } entry)
        {
            return EmployeeError.NotFound;
        }

        if (!Name(name))
        {
            return EmployeeError.Invalid;
        }

        entry.Name = name.Trim();
        entry.IsActive = isActive;
        return EmployeeError.None;
    }
}
