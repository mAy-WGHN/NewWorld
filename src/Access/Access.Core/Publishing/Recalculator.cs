using Access.Contracts;
using Access.Core.Acl;
using Access.Core.Messaging;
using Access.Core.Responsibilities;

namespace Access.Core.Publishing;

public sealed record RecalculationResult(IReadOnlyList<PermissionsChanged> Permissions, IReadOnlyList<ResponsibilitiesChanged> Responsibilities);

/// <summary>
/// Recalculates permissions and responsibilities of every person and publishes only what changed, with a strictly rising
/// version per person. Runs after every change and periodically, so time-limited assignments and exit dates take effect on time.
/// </summary>
public sealed class Recalculator(
    IAclReader reader,
    IPublicationStore publications,
    IEventPublisher publisher,
    TimeProvider time,
    AccessOptions options)
{
    public async Task<RecalculationResult> RecalculateAllAsync(CancellationToken cancellationToken)
    {
        var data = await reader.LoadAsync(cancellationToken);
        var published = await publications.LoadAllAsync(cancellationToken);
        var now = time.GetUtcNow();
        var zone = options.Zone;
        var correlationId = Guid.CreateVersion7(now);
        var permissions = new List<PermissionsChanged>();
        var responsibilities = new List<ResponsibilitiesChanged>();

        foreach (var person in data.People)
        {
            var effective = EffectivePermissionCalculator.Calculate(person.EmployeeId, data, now, zone);
            var grants = effective.Privileges
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new PrivilegeGrant(p.Key, p.Value.Unrestricted, p.Value.StoreIds.Order().ToList(), p.Value.EmployeeIds.Order().ToList()))
                .ToList();
            var permissionPrint = Fingerprint.Of(grants
                .ToDictionary(g => g.Key, g => g.StoreIds.Select(s => "s" + s).Concat(g.EmployeeIds.Select(e => "e" + e)).Append(g.Unrestricted ? "*" : "").ToArray())
                .Append(new KeyValuePair<string, string[]>("#roles", effective.Roles.ToArray()))
                .Append(new KeyValuePair<string, string[]>("#main", [person.HomeStoreId?.ToString() ?? ""]))
                .ToDictionary());
            if (await PublishIfChanged(person.EmployeeId, PublicationKind.Permissions, permissionPrint, published, cancellationToken) is { } permissionVersion)
            {
                var message = new PermissionsChanged(
                    Guid.CreateVersion7(now), correlationId, now, person.EmployeeId, permissionVersion, [],
                    effective.StoreIds.Order().ToList(), effective.EmployeeIds.Order().ToList(), effective.Roles, grants, person.HomeStoreId);
                publisher.Publish(message);
                permissions.Add(message);
            }

            var current = ResponsibilityCalculator.Calculate(person.EmployeeId, data, now, zone);
            var responsibilityPrint = Fingerprint.Of(current.ToDictionary(c => c.Key, c => c.Value.Select(id => id.ToString()).ToArray()));
            if (await PublishIfChanged(person.EmployeeId, PublicationKind.Responsibilities, responsibilityPrint, published, cancellationToken) is { } responsibilityVersion)
            {
                var message = new ResponsibilitiesChanged(
                    Guid.CreateVersion7(now), correlationId, now, person.EmployeeId, responsibilityVersion,
                    current.Select(c => new TopicResponsibility(c.Key, c.Value)).ToList());
                publisher.Publish(message);
                responsibilities.Add(message);
            }
        }

        return new RecalculationResult(permissions, responsibilities);
    }

    private async Task<long?> PublishIfChanged(
        Guid employeeId,
        PublicationKind kind,
        string fingerprint,
        IReadOnlyDictionary<(Guid EmployeeId, PublicationKind Kind), Publication> published,
        CancellationToken cancellationToken)
    {
        if (Publication.Next(published.GetValueOrDefault((employeeId, kind)), fingerprint) is not { } next)
        {
            return null;
        }

        await publications.SaveAsync(employeeId, kind, next, cancellationToken);
        return next.Version;
    }
}
