using Access.Core.Acl;
using Access.Core.People;
using Access.Core.Publishing;
using Access.Core.Responsibilities;
using Microsoft.EntityFrameworkCore;

namespace Access.Infrastructure.Acl;

public sealed class EfAclReader(AccessDbContext db) : IAclReader
{
    public async Task<AclData> LoadAsync(CancellationToken cancellationToken)
    {
        var people = await db.People.AsNoTracking().ToListAsync(cancellationToken);
        var catalog = await db.Catalog.AsNoTracking().ToListAsync(cancellationToken);
        var roles = await db.Roles.AsNoTracking().ToListAsync(cancellationToken);
        var scopes = await db.Scopes.AsNoTracking().ToListAsync(cancellationToken);
        var assignments = await db.Assignments.AsNoTracking().ToListAsync(cancellationToken);
        var responsibilities = await db.Responsibilities.AsNoTracking().ToListAsync(cancellationToken);

        return new AclData(
            people.Select(p => new Person(p.EmployeeId, p.PersonnelNumber, p.Name, p.HomeStoreId, p.Position, p.ExitDate)).ToList(),
            catalog.Select(c => new CatalogEntry(c.Key, c.Owner, c.Scopable)).ToList(),
            roles.Select(r => new Role(r.Id, r.Name, r.Privileges.ToHashSet(StringComparer.Ordinal), r.BuiltIn)).ToList(),
            scopes.Select(s => new ScopeDefinition(s.Id, s.Name, s.StoreIds.ToHashSet())).ToList(),
            assignments.Select(ToAssignment).ToList(),
            responsibilities.Select(ToResponsibility).ToList());
    }

    internal static Assignment ToAssignment(AssignmentRecord a) => new(
        a.Id, a.EmployeeId, a.RoleId,
        new AssignmentReach(a.Unrestricted, a.ScopeIds.ToHashSet(), a.IncludeHomeStore),
        a.ValidFrom, a.ValidTo,
        Enum.TryParse<AssignmentSource>(a.Source, out var source) ? source : AssignmentSource.Unknown);

    internal static Responsibility ToResponsibility(ResponsibilityRecord r) => new(
        r.Id, r.Topic, r.ResponsibleEmployeeId,
        r.TargetKind switch
        {
            nameof(ResponsibilityTarget.Employee) => ResponsibilityTarget.ForEmployee(r.TargetId),
            nameof(ResponsibilityTarget.Store) => ResponsibilityTarget.ForStore(r.TargetId),
            _ => ResponsibilityTarget.ForScope(r.TargetId),
        },
        r.ValidFrom, r.ValidTo);

    internal static (string Kind, Guid Id) FromTarget(ResponsibilityTarget target) => target switch
    {
        ResponsibilityTarget.Employee e => (nameof(ResponsibilityTarget.Employee), e.EmployeeId),
        ResponsibilityTarget.Store s => (nameof(ResponsibilityTarget.Store), s.StoreId),
        ResponsibilityTarget.Scope sc => (nameof(ResponsibilityTarget.Scope), sc.ScopeId),
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };
}
