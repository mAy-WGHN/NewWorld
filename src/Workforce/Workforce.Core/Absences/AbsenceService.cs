using System.Globalization;
using Notifications.Contracts;
using Shared.Approvals;
using Workforce.Contracts;
using Workforce.Core.Certificates;

namespace Workforce.Core.Absences;

/// <summary>
/// The use cases of absences (sick notes and vacation), free of infrastructure. Callers check authorization first (who may do this for
/// whom) and save the unit of work afterwards: absences, approvals and all messages (sync to BC, availability, notifications) are
/// committed together (outbox). Business failures are returned as <see cref="AbsenceError"/>, nothing is stored or published then.
/// </summary>
public sealed class AbsenceService(
    IAbsenceStore store,
    IPeopleDirectory people,
    IEntitlementStore entitlements,
    IResponsibilityDirectory responsibilities,
    IWorkforceMessages messages,
    TimeProvider time,
    WorkforceOptions options,
    IHolidayCalendar holidays)
{
    private const string SickNoteTemplate = "workforce.sick-note.store-manager";
    private const string VacationRequestTemplate = "workforce.vacation-request.supervisor";
    private const string VacationDecisionTemplate = "workforce.vacation-decision.requester";
    private const string ApprovalSubject = "Vacation";

    private WorkCalendar CalendarFor(PersonInfo? person) => new(options.Week, holidays, options.HolidayState, person?.StoreCity);

    private async Task<WorkCalendar> CalendarForAsync(Guid employeeId, CancellationToken cancellationToken) =>
        CalendarFor(await people.GetAsync(employeeId, cancellationToken));

    /// <summary>Today's date in the local time zone (not UTC): the day changes at local midnight, also on summer/winter time days.</summary>
    public DateOnly TodayLocal => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), options.Zone).DateTime);

    // ------------------------------------------------------------------ sickness

    /// <summary>Records a sick note (no approval). Sickness wins over vacation: affected vacation days are cancelled and credited.</summary>
    public async Task<AbsenceResult> ReportSickAsync(Guid employeeId, DateOnly from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (await people.GetAsync(employeeId, cancellationToken) is not { } person)
        {
            return AbsenceResult.Fail(AbsenceError.UnknownPerson);
        }

        if (to is { } end && end < from)
        {
            return AbsenceResult.Fail(AbsenceError.InvalidPeriod);
        }

        if (person.ExitDate is { } exit && from > exit)
        {
            return AbsenceResult.Fail(AbsenceError.AfterExit);
        }

        await store.LockEmployeeAsync(employeeId, cancellationToken);
        var existing = await store.ForEmployeeAsync(employeeId, cancellationToken);
        var range = new DateRange(from, to);
        if (existing.Any(a => a is { Type: AbsenceType.Sickness, Status: AbsenceStatus.Reported } && a.Range.Overlaps(range)))
        {
            return AbsenceResult.Fail(AbsenceError.Overlap);
        }

        var now = time.GetUtcNow();
        var correlationId = Guid.CreateVersion7(now);
        var sick = NewAbsence(employeeId, AbsenceType.Sickness, from, to, AbsenceStatus.Reported, now);
        store.Add(sick);

        foreach (var vacation in existing.Where(a => a.Type == AbsenceType.Vacation && a.Status is AbsenceStatus.Requested or AbsenceStatus.Approved && a.Range.Overlaps(range)))
        {
            await AdjustVacationAsync(vacation, range, now, correlationId, cancellationToken);
        }

        Sync(sick, correlationId, now);
        Availability(employeeId, from, to, AvailabilityStatus.Unavailable, correlationId, now);
        Notify(
            SickNoteTemplate, await ApproversAsync(employeeId, cancellationToken), sick, correlationId, now,
            new Dictionary<string, string> { ["employeeName"] = person.Name, ["from"] = Iso(from), ["to"] = to is { } t ? Iso(t) : "unbefristet" });
        return AbsenceResult.Ok(sick);
    }

    /// <summary>Gives an open-ended sick note its last day.</summary>
    public async Task<AbsenceResult> EndSickNoteAsync(Guid absenceId, DateOnly to, CancellationToken cancellationToken)
    {
        if (await store.GetAsync(absenceId, cancellationToken) is not { } absence)
        {
            return AbsenceResult.Fail(AbsenceError.NotFound);
        }

        if (absence is not { Type: AbsenceType.Sickness, Status: AbsenceStatus.Reported, To: null })
        {
            return AbsenceResult.Fail(AbsenceError.InvalidState);
        }

        if (to < absence.From)
        {
            return AbsenceResult.Fail(AbsenceError.InvalidPeriod);
        }

        await store.LockEmployeeAsync(absence.EmployeeId, cancellationToken);
        var now = time.GetUtcNow();
        var correlationId = Guid.CreateVersion7(now);
        absence.To = to;
        absence.Touch(now);
        Sync(absence, correlationId, now);
        Availability(absence.EmployeeId, absence.From, to, AvailabilityStatus.Unavailable, correlationId, now);
        return AbsenceResult.Ok(absence);
    }

    /// <summary>
    /// Changes the period of a sick note because a medical certificate says so (the period the person entered from the certificate).
    /// Same rules as reporting: no overlap with another sick note, sickness wins over vacation. The change goes to BC (new version).
    /// The caller has checked that the new period belongs to this sick note.
    /// </summary>
    public async Task<AbsenceError> ChangeSickNotePeriodAsync(Absence sick, DateOnly from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (sick is not { Type: AbsenceType.Sickness, Status: AbsenceStatus.Reported })
        {
            return AbsenceError.InvalidState;
        }

        if (to is { } end && end < from)
        {
            return AbsenceError.InvalidPeriod;
        }

        if (await people.GetAsync(sick.EmployeeId, cancellationToken) is not { } person)
        {
            return AbsenceError.UnknownPerson;
        }

        if (person.ExitDate is { } exit && from > exit)
        {
            return AbsenceError.AfterExit;
        }

        await store.LockEmployeeAsync(sick.EmployeeId, cancellationToken);
        var existing = await store.ForEmployeeAsync(sick.EmployeeId, cancellationToken);
        var range = new DateRange(from, to);
        if (existing.Any(a => a.Id != sick.Id && a is { Type: AbsenceType.Sickness, Status: AbsenceStatus.Reported } && a.Range.Overlaps(range)))
        {
            return AbsenceError.Overlap;
        }

        var now = time.GetUtcNow();
        var correlationId = Guid.CreateVersion7(now);
        foreach (var vacation in existing.Where(a => a.Type == AbsenceType.Vacation && a.Status is AbsenceStatus.Requested or AbsenceStatus.Approved && a.Range.Overlaps(range)).ToList())
        {
            await AdjustVacationAsync(vacation, range, now, correlationId, cancellationToken);
        }

        sick.From = from;
        sick.To = to;
        sick.Touch(now);
        Sync(sick, correlationId, now);
        Availability(sick.EmployeeId, from, to, AvailabilityStatus.Unavailable, correlationId, now);
        return AbsenceError.None;
    }

    // ------------------------------------------------------------------ vacation

    public async Task<AbsenceResult> RequestVacationAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (await people.GetAsync(employeeId, cancellationToken) is not { } person)
        {
            return AbsenceResult.Fail(AbsenceError.UnknownPerson);
        }

        var calendar = CalendarFor(person);
        if (to < from || from < TodayLocal || calendar.CountWorkingDays(from, to) == 0)
        {
            return AbsenceResult.Fail(AbsenceError.InvalidPeriod);
        }

        if (person.ExitDate is { } exit && to > exit)
        {
            return AbsenceResult.Fail(AbsenceError.AfterExit);
        }

        await store.LockEmployeeAsync(employeeId, cancellationToken);
        var existing = await store.ForEmployeeAsync(employeeId, cancellationToken);
        var range = DateRange.Of(from, to);
        if (existing.Any(a => a.IsActive && a.Range.Overlaps(range)))
        {
            return AbsenceResult.Fail(AbsenceError.Overlap);
        }

        var bookings = existing.Where(a => a.Type == AbsenceType.Vacation && a.To is not null).Select(a => new VacationBooking(a.Range, a.Status)).ToList();
        foreach (var (year, days) in calendar.CountWorkingDaysByYear(from, to).Where(y => y.Value > 0))
        {
            if (await entitlements.GetAsync(employeeId, year, cancellationToken) is not { } entitlement)
            {
                return AbsenceResult.Fail(AbsenceError.NoEntitlement);
            }

            if (VacationAccount.Compute(year, entitlement, bookings, TodayLocal, calendar).Remaining < days)
            {
                return AbsenceResult.Fail(AbsenceError.InsufficientBalance);
            }
        }

        var approvers = (await ApproversAsync(employeeId, cancellationToken)).Where(a => a != employeeId).ToList();
        if (approvers.Count == 0)
        {
            return AbsenceResult.Fail(AbsenceError.NoApprover);
        }

        var now = time.GetUtcNow();
        var correlationId = Guid.CreateVersion7(now);
        var vacation = NewAbsence(employeeId, AbsenceType.Vacation, from, to, AbsenceStatus.Requested, now);

        // All responsible persons are alternatives of one step: the store manager or the substitute decides.
        var approval = ApprovalRequest.CreateWithAlternatives(ApprovalSubject, vacation.Id, employeeId, [approvers], now);
        vacation.ApprovalRequestId = approval.Id;
        store.AddApproval(approval);
        store.Add(vacation);

        Sync(vacation, correlationId, now);
        Notify(
            VacationRequestTemplate, approvers, vacation, correlationId, now,
            new Dictionary<string, string> { ["employeeName"] = person.Name, ["from"] = Iso(from), ["to"] = Iso(to) });
        return AbsenceResult.Ok(vacation);
    }

    /// <summary>A person of the approval chain approves or rejects a requested vacation.</summary>
    public async Task<AbsenceResult> DecideVacationAsync(Guid absenceId, Guid approverId, bool approve, CancellationToken cancellationToken)
    {
        if (await store.GetAsync(absenceId, cancellationToken) is not { } vacation)
        {
            return AbsenceResult.Fail(AbsenceError.NotFound);
        }

        if (vacation is not { Type: AbsenceType.Vacation, Status: AbsenceStatus.Requested, ApprovalRequestId: { } approvalId }
            || await store.GetApprovalAsync(approvalId, cancellationToken) is not { } approval)
        {
            return AbsenceResult.Fail(AbsenceError.InvalidState);
        }

        await store.LockEmployeeAsync(vacation.EmployeeId, cancellationToken);
        var now = time.GetUtcNow();
        try
        {
            if (approve)
            {
                approval.Approve(approverId, now);
            }
            else
            {
                approval.Reject(approverId, now);
            }
        }
        catch (ApprovalException ex)
        {
            return AbsenceResult.Fail(ex.Error is ApprovalError.NotCurrentApprover ? AbsenceError.NotAllowed : AbsenceError.InvalidState);
        }

        var correlationId = Guid.CreateVersion7(now);
        vacation.Status = approve ? AbsenceStatus.Approved : AbsenceStatus.Rejected;
        vacation.Touch(now);
        Sync(vacation, correlationId, now);
        if (approve)
        {
            Availability(vacation.EmployeeId, vacation.From, vacation.To, AvailabilityStatus.Unavailable, correlationId, now);
        }

        Notify(
            VacationDecisionTemplate, [vacation.EmployeeId], vacation, correlationId, now,
            new Dictionary<string, string> { ["decision"] = approve ? "genehmigt" : "abgelehnt", ["from"] = Iso(vacation.From), ["to"] = Iso(vacation.To!.Value) });
        return AbsenceResult.Ok(vacation);
    }

    /// <summary>The requester withdraws a vacation: a pending request, or an approved one that has not started yet.</summary>
    public async Task<AbsenceResult> WithdrawVacationAsync(Guid absenceId, Guid byEmployeeId, CancellationToken cancellationToken)
    {
        if (await store.GetAsync(absenceId, cancellationToken) is not { } vacation)
        {
            return AbsenceResult.Fail(AbsenceError.NotFound);
        }

        if (vacation is not { Type: AbsenceType.Vacation, Status: AbsenceStatus.Requested or AbsenceStatus.Approved })
        {
            return AbsenceResult.Fail(AbsenceError.InvalidState);
        }

        if (vacation.EmployeeId != byEmployeeId)
        {
            return AbsenceResult.Fail(AbsenceError.NotAllowed);
        }

        var wasApproved = vacation.Status == AbsenceStatus.Approved;
        if (wasApproved && vacation.From <= TodayLocal)
        {
            return AbsenceResult.Fail(AbsenceError.AlreadyStarted);
        }

        await store.LockEmployeeAsync(vacation.EmployeeId, cancellationToken);
        var now = time.GetUtcNow();
        var correlationId = Guid.CreateVersion7(now);
        await CloseApprovalAsync(vacation, cancellationToken);
        vacation.Status = AbsenceStatus.Withdrawn;
        vacation.Touch(now);
        Sync(vacation, correlationId, now);
        if (wasApproved)
        {
            Availability(vacation.EmployeeId, vacation.From, vacation.To, AvailabilityStatus.Available, correlationId, now);
        }

        return AbsenceResult.Ok(vacation);
    }

    // ------------------------------------------------------------------ exit

    /// <summary>
    /// A person leaves: absences after the exit date are cancelled, those across it end on the exit date, open-ended sick notes end
    /// there. (Absences before the exit date stay as they are.)
    /// </summary>
    public async Task OnEmployeeLeftAsync(Guid employeeId, DateOnly exitDate, CancellationToken cancellationToken)
    {
        await store.LockEmployeeAsync(employeeId, cancellationToken);
        var now = time.GetUtcNow();
        var correlationId = Guid.CreateVersion7(now);
        foreach (var absence in (await store.ForEmployeeAsync(employeeId, cancellationToken)).Where(a => a.IsActive && (a.To is null || a.To > exitDate)).ToList())
        {
            var wasApproved = absence.Status == AbsenceStatus.Approved;
            var removedFrom = absence.From > exitDate ? absence.From : exitDate.AddDays(1);
            var removedTo = absence.To;
            if (absence.From > exitDate)
            {
                await CloseApprovalAsync(absence, cancellationToken);
                absence.Status = AbsenceStatus.Cancelled;
            }
            else
            {
                absence.To = exitDate;
            }

            absence.Touch(now);
            Sync(absence, correlationId, now);
            if (wasApproved && absence.Type == AbsenceType.Vacation)
            {
                Availability(employeeId, removedFrom, removedTo, AvailabilityStatus.Available, correlationId, now);
            }
        }
    }

    // ------------------------------------------------------------------ account

    /// <returns>The balance of the year, or <c>null</c> if no entitlement is maintained for it.</returns>
    public async Task<VacationBalance?> BalanceAsync(Guid employeeId, int year, CancellationToken cancellationToken)
    {
        if (await entitlements.GetAsync(employeeId, year, cancellationToken) is not { } entitlement)
        {
            return null;
        }

        var bookings = (await store.ForEmployeeAsync(employeeId, cancellationToken))
            .Where(a => a.Type == AbsenceType.Vacation && a.To is not null)
            .Select(a => new VacationBooking(a.Range, a.Status));
        return VacationAccount.Compute(year, entitlement, bookings, TodayLocal, await CalendarForAsync(employeeId, cancellationToken));
    }

    public async Task<AbsenceError> SetEntitlementAsync(Guid employeeId, int year, decimal days, CancellationToken cancellationToken)
    {
        if (days is < 0 or > 366 || year is < 2000 or > 2200)
        {
            return AbsenceError.InvalidPeriod;
        }

        if (await people.GetAsync(employeeId, cancellationToken) is null)
        {
            return AbsenceError.UnknownPerson;
        }

        await entitlements.SetAsync(employeeId, year, days, cancellationToken);
        return AbsenceError.None;
    }

    // ------------------------------------------------------------------ helpers

    private async Task AdjustVacationAsync(Absence vacation, DateRange sickness, DateTimeOffset now, Guid correlationId, CancellationToken cancellationToken)
    {
        if (vacation.Status == AbsenceStatus.Requested)
        {
            // A pending request that collides with sickness is cancelled; the person can request again.
            await CloseApprovalAsync(vacation, cancellationToken);
            vacation.Status = AbsenceStatus.Cancelled;
            vacation.Touch(now);
            Sync(vacation, correlationId, now);
            return;
        }

        var plan = SicknessAdjustment.Plan(vacation.Range, sickness, await CalendarForAsync(vacation.EmployeeId, cancellationToken));
        if (plan.IsCompletelyCancelled)
        {
            vacation.Status = AbsenceStatus.Cancelled;
        }
        else if (plan.Before is { } before)
        {
            vacation.To = before.To;
            if (plan.After is { } after)
            {
                var rest = NewAbsence(vacation.EmployeeId, AbsenceType.Vacation, after.From, after.To, AbsenceStatus.Approved, now);
                rest.ApprovalRequestId = vacation.ApprovalRequestId;
                store.Add(rest);
                Sync(rest, correlationId, now);
            }
        }
        else
        {
            vacation.From = plan.After!.Value.From;
            vacation.To = plan.After.Value.To;
        }

        vacation.Touch(now);
        Sync(vacation, correlationId, now);
    }

    private async Task CloseApprovalAsync(Absence vacation, CancellationToken cancellationToken)
    {
        if (vacation is { Status: AbsenceStatus.Requested, ApprovalRequestId: { } id } && await store.GetApprovalAsync(id, cancellationToken) is { } approval)
        {
            approval.Withdraw(vacation.EmployeeId, time.GetUtcNow());
        }
    }

    private async Task<IReadOnlyList<Guid>> ApproversAsync(Guid employeeId, CancellationToken cancellationToken) =>
        (await responsibilities.ApproversAsync(employeeId, cancellationToken)).Distinct().ToList();

    private static Absence NewAbsence(Guid employeeId, AbsenceType type, DateOnly from, DateOnly? to, AbsenceStatus status, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        EmployeeId = employeeId,
        Type = type,
        From = from,
        To = to,
        Status = status,
        CertificateStatus = type == AbsenceType.Sickness ? Certificates.CertificateStatus.Missing : Certificates.CertificateStatus.Unknown,
        Version = 1,
        SyncStatus = SyncStatus.Pending,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private void Sync(Absence absence, Guid correlationId, DateTimeOffset now) =>
        messages.Send(new SyncAbsenceToBc(
            Guid.CreateVersion7(now), correlationId, now, absence.Id, absence.EmployeeId, absence.From, absence.To, absence.Type, absence.Status, absence.Version));

    /// <summary>Neutralised: no reason, no type.</summary>
    private void Availability(Guid employeeId, DateOnly from, DateOnly? to, AvailabilityStatus status, Guid correlationId, DateTimeOffset now) =>
        messages.Publish(new EmployeeAvailabilityChanged(Guid.CreateVersion7(now), correlationId, now, employeeId, from, to, status));

    /// <summary>
    /// Class 2 data (name and period of an absence) only for in-app delivery. The notification ID is derived from absence, version
    /// and template, so a repeated command is recognised by the Notification service.
    /// </summary>
    private void Notify(
        string templateKey, IReadOnlyList<Guid> recipients, Absence absence, Guid correlationId, DateTimeOffset now, IReadOnlyDictionary<string, string> data)
    {
        if (recipients.Count > 0)
        {
            messages.Send(new SendNotification(
                Guid.CreateVersion7(now), correlationId, now,
                DeterministicGuid.From($"{absence.Id}:{absence.Version}:{templateKey}"),
                recipients, templateKey, data, [NotificationChannel.InApp], $"/absences/{absence.Id}"));
        }
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
