using Access.Core.Acl;

namespace Access.Core.Responsibilities;

/// <summary>Current responsible persons per topic for one person: all valid responsibilities targeting them, their home store or a scope containing it.</summary>
public static class ResponsibilityCalculator
{
    public static IReadOnlyDictionary<string, IReadOnlyList<Guid>> Calculate(Guid employeeId, AclData data, DateTimeOffset at, TimeZoneInfo zone)
    {
        var person = data.People.FirstOrDefault(p => p.EmployeeId == employeeId);
        if (person is null || !person.IsActiveAt(at, zone))
        {
            return new Dictionary<string, IReadOnlyList<Guid>>();
        }

        var active = data.People.Where(p => p.IsActiveAt(at, zone)).Select(p => p.EmployeeId).ToHashSet();
        var scopes = data.Scopes.ToDictionary(s => s.Id);
        bool Targets(ResponsibilityTarget target) => target switch
        {
            ResponsibilityTarget.Employee e => e.EmployeeId == employeeId,
            ResponsibilityTarget.Store s => s.StoreId == person.HomeStoreId,
            ResponsibilityTarget.Scope sc => person.HomeStoreId is { } home && scopes.TryGetValue(sc.ScopeId, out var scope) && scope.StoreIds.Contains(home),
            _ => false,
        };

        return data.Responsibilities
            .Where(r => r.IsValidAt(at) && r.ResponsibleEmployeeId != employeeId && active.Contains(r.ResponsibleEmployeeId) && Targets(r.Target))
            .GroupBy(r => r.Topic, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(r => r.ResponsibleEmployeeId).Distinct().Order().ToList(), StringComparer.Ordinal);
    }
}
