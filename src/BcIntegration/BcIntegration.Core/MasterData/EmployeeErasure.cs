using BcIntegration.Contracts;
using BcIntegration.Core.Messaging;

namespace BcIntegration.Core.MasterData;

/// <summary>
/// After a (for now manual) release: publishes <see cref="EmployeeDataErasureRequested"/> to every service with copies,
/// deletes the stored state here and blocks future copies of this person. Retention rules are out of scope.
/// </summary>
public sealed class EmployeeErasure(IMasterDataStore store, IEventPublisher publisher, TimeProvider time)
{
    /// <returns><c>false</c> if the employee is unknown here.</returns>
    public async Task<bool> RequestAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (await store.GetEmployeeAsync(employeeId, cancellationToken) is null)
        {
            return false;
        }

        await store.MarkErasedAsync(employeeId, cancellationToken);
        var id = MessageIds.New(time);
        publisher.Publish(new EmployeeDataErasureRequested(id, id, time.GetUtcNow(), employeeId));
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }
}
