using BcIntegration.Core.Errors;
using BcIntegration.Core.Workforce;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure.Messaging;
using Workforce.Contracts;

namespace BcIntegration.Infrastructure.Workforce;

/// <summary>
/// Receives <see cref="SyncAbsenceToBc"/> from its queue (readable only by BC Integration). Permanent technical BC failures
/// go to the dead-letter queue; transient ones lead to redelivery; business failures are answered with <c>BcSyncFailed</c>.
/// </summary>
public sealed partial class SyncAbsenceToBcHandler(AbsenceSynchronizer synchronizer, ILogger<SyncAbsenceToBcHandler> logger)
    : IMessageHandler<SyncAbsenceToBc>
{
    public async Task HandleAsync(SyncAbsenceToBc message, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await synchronizer.SyncAsync(message, cancellationToken);
            LogOutcome(message.AbsenceId, message.Version, outcome);
        }
        catch (BcTechnicalFailureException ex)
        {
            throw new PermanentMessageFailureException(ex.Reason);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Absence {AbsenceId} version {Version}: {Outcome}")]
    private partial void LogOutcome(Guid absenceId, long version, AbsenceSyncOutcome outcome);
}
