using Shared.Messaging;

namespace Workforce.Contracts;

/// <summary>
/// Order to write an absence to Business Central (upsert via <paramref name="AbsenceId"/> as external ID). Contains the
/// absence type because BC is its archive; travels only through a queue that only BC Integration may read.
/// BC Integration discards versions older than the last one written and answers with <c>BcSyncSucceeded</c>/<c>BcSyncFailed</c>.
/// </summary>
/// <remarks>Publisher: Workforce. Consumers: BC Integration.</remarks>
/// <param name="MessageId">Unique message ID (idempotency).</param>
/// <param name="CorrelationId">Correlates all messages of one flow; repeated in the sync result.</param>
/// <param name="OccurredAt">Time of the change (TimeProvider).</param>
/// <param name="AbsenceId">Internal ID of the absence, external ID in BC.</param>
/// <param name="EmployeeId">Person concerned.</param>
/// <param name="From">First day.</param>
/// <param name="To">Last day; <c>null</c> for "until further notice" (sick note). TODO(BC-Modell): how BC stores open-ended absences.</param>
/// <param name="Type">Kind of absence. TODO(BC-Modell): mapping to BC absence codes (e.g. KRANK, URLAUB).</param>
/// <param name="Status">Current status. TODO(BC-Modell): mapping to BC approval states.</param>
/// <param name="Version">Version of the absence; every change increases it.</param>
[Sensitivity(SensitivityClass.SensitiveFacts)]
public sealed record SyncAbsenceToBc(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    Guid AbsenceId,
    Guid EmployeeId,
    DateOnly From,
    DateOnly? To,
    AbsenceType Type,
    AbsenceStatus Status,
    long Version) : ICommand;
