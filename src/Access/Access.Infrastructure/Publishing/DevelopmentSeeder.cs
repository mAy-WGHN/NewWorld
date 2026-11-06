using System.Text.Json;
using Access.Core;
using Access.Core.Acl;
using Access.Core.Responsibilities;
using Access.Infrastructure.Acl;
using Microsoft.EntityFrameworkCore;

namespace Access.Infrastructure.Publishing;

/// <summary>
/// Only for the local landscape (the AppHost sets <see cref="DevelopmentSeedOptions"/>; no other environment does): makes a fresh start usable.
/// The roles of the role file are created if they are missing (an existing role is only filled if it has no privileges, nothing is overwritten),
/// and the configured people become the HR team: they get the HR role and are responsible for everybody on the topics of the HR processes, so a
/// vacation request finds its approver. Runs inside the recalculation transaction and is idempotent. Roles are written directly, without the
/// catalogue check: privileges of services that have not registered yet become effective as soon as they do (unregistered ones have no effect).
/// </summary>
public static class DevelopmentSeeder
{
    /// <summary>Topics the HR team is responsible for (decisions 17, 30, 32, 37).</summary>
    public static readonly string[] HrTopics = ["absence.approval", "employee.change-request", "schedule.adjustment", "workingtime.payout"];

    public static async Task EnsureAsync(AccessDbContext db, DevelopmentSeedOptions options, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!options.IsActive)
        {
            return;
        }

        var hrRole = await EnsureRolesAsync(db, options, time, cancellationToken);
        if (hrRole is null || options.HrPersonnelNumbers.Count == 0)
        {
            return;
        }

        var people = await db.People.Where(p => options.HrPersonnelNumbers.Contains(p.PersonnelNumber)).Select(p => p.EmployeeId).ToListAsync(cancellationToken);
        foreach (var employeeId in people)
        {
            if (!Has(db.Assignments, a => a.EmployeeId == employeeId && a.RoleId == hrRole.Id && a.Source == nameof(AssignmentSource.Seed)))
            {
                db.Assignments.Add(new AssignmentRecord
                {
                    Id = Guid.CreateVersion7(time.GetUtcNow()),
                    EmployeeId = employeeId,
                    RoleId = hrRole.Id,
                    Unrestricted = true,
                    Source = nameof(AssignmentSource.Seed),
                });
            }

            foreach (var topic in HrTopics)
            {
                if (!Has(db.Responsibilities, r => r.ResponsibleEmployeeId == employeeId && r.Topic == topic && r.TargetKind == nameof(ResponsibilityTarget.EveryoneTarget)))
                {
                    db.Responsibilities.Add(new ResponsibilityRecord
                    {
                        Id = Guid.CreateVersion7(time.GetUtcNow()),
                        Topic = topic,
                        ResponsibleEmployeeId = employeeId,
                        TargetKind = nameof(ResponsibilityTarget.EveryoneTarget),
                        TargetId = Guid.Empty,
                    });
                }
            }
        }
    }

    /// <summary>Creates the roles of the file that do not exist yet; returns the HR role (existing or created).</summary>
    private static async Task<RoleRecord?> EnsureRolesAsync(AccessDbContext db, DevelopmentSeedOptions options, TimeProvider time, CancellationToken cancellationToken)
    {
        var hr = await FindRoleAsync(db, options.HrRoleName, cancellationToken);
        if (string.IsNullOrWhiteSpace(options.RoleFile) || !File.Exists(options.RoleFile))
        {
            return hr;
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(options.RoleFile, cancellationToken));
        foreach (var role in document.RootElement.GetProperty("roles").EnumerateArray())
        {
            var name = role.GetProperty("name").GetString()!;
            var privileges = role.GetProperty("privileges").EnumerateArray().Select(p => p.GetString()!).ToList();
            var existing = await FindRoleAsync(db, name, cancellationToken);
            if (existing is null)
            {
                existing = new RoleRecord { Id = Guid.CreateVersion7(time.GetUtcNow()), Name = name, Privileges = privileges, BuiltIn = false };
                db.Roles.Add(existing);
            }
            else if (existing.Privileges.Count == 0 && existing.Id != Role.AdministratorId && existing.Id != Role.FormerEmployeeId)
            {
                existing.Privileges = privileges;
            }

            if (string.Equals(name, options.HrRoleName, StringComparison.OrdinalIgnoreCase))
            {
                hr = existing;
            }
        }

        return hr;
    }

    private static async Task<RoleRecord?> FindRoleAsync(AccessDbContext db, string name, CancellationToken cancellationToken) =>
        db.Roles.Local.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? (await db.Roles.ToListAsync(cancellationToken)).FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a matching row exists in the database or is already added in this run.</summary>
    private static bool Has<T>(DbSet<T> set, Func<T, bool> predicate) where T : class => set.Local.Any(predicate) || set.AsEnumerable().Any(predicate);
}
