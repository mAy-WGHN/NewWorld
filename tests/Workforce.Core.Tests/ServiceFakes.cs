using Shared.Approvals;
using Shared.Messaging;
using Workforce.Core.Absences;

namespace Workforce.Core.Tests;

internal sealed class FakeStore : IAbsenceStore
{
    public List<Absence> Absences { get; } = [];
    public Dictionary<Guid, ApprovalRequest> Approvals { get; } = [];

    public Task LockEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<Absence>> ForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Absence>>(Absences.Where(a => a.EmployeeId == employeeId).ToList());

    public Task<Absence?> GetAsync(Guid absenceId, CancellationToken cancellationToken) => Task.FromResult(Absences.FirstOrDefault(a => a.Id == absenceId));

    public void Add(Absence absence) => Absences.Add(absence);

    public Task<ApprovalRequest?> GetApprovalAsync(Guid approvalId, CancellationToken cancellationToken) =>
        Task.FromResult(Approvals.GetValueOrDefault(approvalId));

    public void AddApproval(ApprovalRequest approval) => Approvals[approval.Id] = approval;
}

internal sealed class FakePeople : IPeopleDirectory
{
    public Dictionary<Guid, PersonInfo> People { get; } = [];

    public Task<PersonInfo?> GetAsync(Guid employeeId, CancellationToken cancellationToken) => Task.FromResult(People.GetValueOrDefault(employeeId));
}

internal sealed class FakeEntitlements : IEntitlementStore
{
    public Dictionary<(Guid, int), decimal> Days { get; } = [];

    public Task<decimal?> GetAsync(Guid employeeId, int year, CancellationToken cancellationToken) =>
        Task.FromResult(Days.TryGetValue((employeeId, year), out var d) ? d : (decimal?)null);

    public Task SetAsync(Guid employeeId, int year, decimal days, CancellationToken cancellationToken)
    {
        Days[(employeeId, year)] = days;
        return Task.CompletedTask;
    }
}

internal sealed class FakeExtraVacation : IExtraVacationStore
{
    public List<(Guid EmployeeId, ExtraVacationDays Extra, Guid CreatedBy)> Items { get; } = [];

    public List<(Guid EmployeeId, EntitlementChange Change)> Changes { get; } = [];

    public Task<IReadOnlyList<EntitlementChange>> ChangesAsync(Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EntitlementChange>>([.. Changes.Where(c => c.EmployeeId == employeeId).Select(c => c.Change)]);

    public void AddChange(Guid employeeId, EntitlementChange change, Guid createdBy, DateTimeOffset at) => Changes.Add((employeeId, change));

    public Task<bool> RemoveChangeAsync(Guid employeeId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Changes.RemoveAll(c => c.EmployeeId == employeeId && c.Change.Id == id) > 0);

    public VacationRulesSetting? Rules { get; set; }

    public Task<VacationRulesSetting?> GetRulesAsync(CancellationToken cancellationToken) => Task.FromResult(Rules);

    public Task SetRulesAsync(VacationRulesSetting setting, CancellationToken cancellationToken)
    {
        Rules = setting;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExtraVacationDays>> ForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExtraVacationDays>>([.. Items.Where(i => i.EmployeeId == employeeId).Select(i => i.Extra)]);

    public void Add(Guid employeeId, ExtraVacationDays extra, Guid createdBy, DateTimeOffset at) => Items.Add((employeeId, extra, createdBy));

    public Task<bool> RemoveAsync(Guid employeeId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.RemoveAll(i => i.EmployeeId == employeeId && i.Extra.Id == id) > 0);
}

internal sealed class FakeResponsibilities : IResponsibilityDirectory
{
    public Dictionary<Guid, List<Guid>> Approvers { get; } = [];

    public Task<IReadOnlyList<Guid>> ApproversAsync(Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Approvers.GetValueOrDefault(employeeId) ?? []);
}

internal sealed class RecordingMessages : IWorkforceMessages
{
    public List<IMessage> Sent { get; } = [];

    public void Publish(IEvent message) => Sent.Add(message);

    public void Send(ICommand message) => Sent.Add(message);

    public IEnumerable<T> Of<T>() where T : IMessage => Sent.OfType<T>();
}
