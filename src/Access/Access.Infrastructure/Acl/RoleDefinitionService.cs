using Access.Core;
using Access.Core.Acl;
using Microsoft.EntityFrameworkCore;

namespace Access.Infrastructure.Acl;

/// <summary>One own privilege in a role definition file (the registered ones of the services come from the services themselves).</summary>
public sealed record CustomPrivilegeDefinition(string Key, bool Scopable, string? Description);

/// <summary>One role in a role definition file: its name and its privilege patterns.</summary>
public sealed record RoleDefinition(string Name, IReadOnlyList<string> Privileges);

/// <summary>
/// The roles of an environment as a file (<c>version</c> 1): own privileges and roles with their privilege patterns. Nothing environment-specific is in it
/// (no people, assignments, store groups or responsibilities), so the file made locally can be imported in production.
/// </summary>
public sealed record RoleDefinitionDocument(int Version, IReadOnlyList<CustomPrivilegeDefinition> CustomPrivileges, IReadOnlyList<RoleDefinition> Roles);

/// <summary>What an import would do (or did) to one item. <c>Action</c>: create, update, unchanged or error; <c>Added</c>/<c>Removed</c> are the diff to the existing role.</summary>
public sealed record RoleImportItem(string Kind, string Name, string Action, IReadOnlyList<string> Added, IReadOnlyList<string> Removed, string? Error);

/// <summary>The outcome of an import. With errors nothing is changed.</summary>
public sealed record RoleImportResult(bool Applied, bool HasErrors, IReadOnlyList<RoleImportItem> Items);

/// <summary>
/// Export and import of roles. A comparison (no <c>apply</c>) only shows the diff against the existing roles and changes nothing. When applied, roles are
/// matched by name (case-insensitive): a missing role is created, an existing one gets exactly the privileges of the file, roles that are not in the file stay
/// untouched (nothing is deleted). Only the selected items are applied (all if none are selected). The whole file is checked first; a file with errors is not applied.
/// Every change goes through <see cref="AclAdministration"/>, so the protection rules of the acting person apply (no granting of more than one has).
/// </summary>
public sealed class RoleDefinitionService(AccessDbContext db, AclAdministration admin)
{
    public const int CurrentVersion = 1;
    public const string KindRole = "role";
    public const string KindCustomPrivilege = "customPrivilege";

    public async Task<RoleDefinitionDocument> ExportAsync(CancellationToken cancellationToken)
    {
        var custom = await db.Catalog.Where(c => c.Owner == AclAdministration.CustomOwner).OrderBy(c => c.Key).ToListAsync(cancellationToken);
        var roles = await db.Roles.Where(r => r.Id != Role.AdministratorId).ToListAsync(cancellationToken);
        return new RoleDefinitionDocument(
            CurrentVersion,
            custom.Select(c => new CustomPrivilegeDefinition(c.Key, c.Scopable, c.Description)).ToList(),
            roles.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(r => new RoleDefinition(r.Name, r.Privileges.Order(StringComparer.Ordinal).ToList()))
                .ToList());
    }

    // selected: names (roles) or keys (own privileges) to apply; null or empty means everything that differs.
    public async Task<RoleImportResult> ImportAsync(
        Guid actor, RoleDefinitionDocument? document, bool apply, IReadOnlyCollection<string>? selected, CancellationToken cancellationToken)
    {
        if (document is null || document.Version != CurrentVersion)
        {
            return new RoleImportResult(false, true, [Error("file", "Datei", "UnsupportedVersion")]);
        }

        var catalog = await db.Catalog.ToListAsync(cancellationToken);
        var existingRoles = await db.Roles.ToListAsync(cancellationToken);
        var items = new List<RoleImportItem>();
        var newCustom = new List<CustomPrivilegeDefinition>();
        var knownKeys = catalog.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var privilege in document.CustomPrivileges ?? [])
        {
            if (!PrivilegePattern.IsValidPattern(privilege.Key) || privilege.Key.EndsWith('*'))
            {
                items.Add(Error(KindCustomPrivilege, privilege.Key, "Invalid"));
            }
            else if (catalog.FirstOrDefault(c => c.Key == privilege.Key) is { } found)
            {
                items.Add(found.Owner == AclAdministration.CustomOwner
                    ? new RoleImportItem(KindCustomPrivilege, privilege.Key, "unchanged", [], [], null)
                    : Error(KindCustomPrivilege, privilege.Key, "InUse"));
            }
            else
            {
                newCustom.Add(privilege);
                knownKeys.Add(privilege.Key);
                items.Add(new RoleImportItem(KindCustomPrivilege, privilege.Key, "create", [], [], null));
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roleChanges = new List<(RoleRecord? Existing, string Name, HashSet<string> Privileges)>();
        foreach (var role in document.Roles ?? [])
        {
            var name = role.Name?.Trim() ?? string.Empty;
            var privileges = (role.Privileges ?? []).ToHashSet(StringComparer.Ordinal);
            if (name.Length == 0 || !seen.Add(name))
            {
                items.Add(Error(KindRole, name, name.Length == 0 ? "Invalid" : "Duplicate"));
                continue;
            }

            var existing = existingRoles.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing?.Id == Role.AdministratorId)
            {
                items.Add(Error(KindRole, name, "BuiltInRole"));
                continue;
            }

            if (privileges.FirstOrDefault(p => !PrivilegePattern.IsKnown(p, knownKeys)) is { } unknown)
            {
                items.Add(Error(KindRole, name, "UnknownPrivilege: " + unknown));
                continue;
            }

            if (existing is null)
            {
                items.Add(new RoleImportItem(KindRole, name, "create", privileges.Order(StringComparer.Ordinal).ToList(), [], null));
                roleChanges.Add((null, name, privileges));
            }
            else if (existing.Privileges.ToHashSet(StringComparer.Ordinal).SetEquals(privileges))
            {
                items.Add(new RoleImportItem(KindRole, existing.Name, "unchanged", [], [], null));
            }
            else
            {
                items.Add(new RoleImportItem(
                    KindRole,
                    existing.Name,
                    "update",
                    privileges.Except(existing.Privileges).Order(StringComparer.Ordinal).ToList(),
                    existing.Privileges.Except(privileges).Order(StringComparer.Ordinal).ToList(),
                    null));
                roleChanges.Add((existing, existing.Name, privileges));
            }
        }

        var hasErrors = items.Any(i => i.Action == "error");
        if (hasErrors || !apply)
        {
            return new RoleImportResult(false, hasErrors, items);
        }

        bool Chosen(string name) => selected is null || selected.Count == 0 || selected.Contains(name, StringComparer.OrdinalIgnoreCase);

        foreach (var privilege in newCustom.Where(p => Chosen(p.Key)))
        {
            var result = await admin.CreateCustomPrivilegeAsync(actor, privilege.Key, privilege.Scopable, privilege.Description, cancellationToken);
            if (!result.Guard.IsAllowed)
            {
                return Failed(items, KindCustomPrivilege, privilege.Key, result.Guard.Reason.ToString());
            }
        }

        foreach (var (existing, name, privileges) in roleChanges.Where(c => Chosen(c.Name)))
        {
            var result = existing is null
                ? await admin.CreateRoleAsync(actor, name, privileges, cancellationToken)
                : await admin.UpdateRoleAsync(actor, existing.Id, privileges, cancellationToken);
            if (!result.Guard.IsAllowed)
            {
                return Failed(items, KindRole, name, result.Guard.Reason.ToString());
            }
        }

        return new RoleImportResult(true, false, items);
    }

    private static RoleImportResult Failed(List<RoleImportItem> items, string kind, string name, string reason) =>
        new(false, true, [.. items.Select(i => i.Kind == kind && i.Name == name ? i with { Action = "error", Error = reason } : i)]);

    private static RoleImportItem Error(string kind, string name, string reason) => new(kind, name, "error", [], [], reason);
}
