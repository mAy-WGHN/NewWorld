using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shared.Authorization;

namespace Shared.Infrastructure.Audit;

/// <summary>
/// Writes an audit line for every created, changed or deleted record of a request (person, calling service or anonymous caller) into the same
/// transaction as the change itself. Changes without a request (message handlers, background services) are not audited: they are no actions of a person.
/// Never audited: the types in <see cref="AuditOptions"/> and property values whose name says secret (see <see cref="AuditOptions.IsSecret"/>).
/// </summary>
public sealed class AuditSaveChangesInterceptor(
    IHttpContextAccessor httpContext,
    TimeProvider time,
    AuditOptions options,
    IAuditPersonLookup lookup) : SaveChangesInterceptor
{
    private const int MaxValueLength = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    // Synchronous saves are not used by the services; the actor is then logged by id only.
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } db)
        {
            Record(db, null);
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } db)
        {
            db.ChangeTracker.DetectChanges();
            RefuseChangesToTheLog(db);
            AuditPerson? person = null;
            if (httpContext.HttpContext?.User.FindFirst(AppClaimTypes.EmployeeId)?.Value is { } id
                && Guid.TryParse(id, out var employeeId)
                && db.ChangeTracker.Entries().Any(Audited))
            {
                person = await lookup.FindAsync(db, employeeId, cancellationToken);
            }

            Record(db, person);
        }

        return result;
    }

    private static void RefuseChangesToTheLog(DbContext db)
    {
        if (db.ChangeTracker.Entries<AuditEntryRecord>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("The audit log is append only.");
        }
    }

    private bool Audited(EntityEntry entry) =>
        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && !options.IsExcluded(entry.Metadata.ClrType);

    private void Record(DbContext db, AuditPerson? person)
    {
        db.ChangeTracker.DetectChanges();
        RefuseChangesToTheLog(db);
        if (httpContext.HttpContext is not { } context)
        {
            return;
        }

        var entries = db.ChangeTracker.Entries().Where(Audited).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var (kind, employeeId, number, name) = Actor(context, person);
        var now = time.GetUtcNow();
        foreach (var entry in entries)
        {
            var changes = Changes(entry);
            if (entry.State == EntityState.Modified && changes is null)
            {
                continue;
            }

            db.Set<AuditEntryRecord>().Add(new AuditEntryRecord
            {
                OccurredAt = now,
                ActorKind = kind,
                ActorEmployeeId = employeeId,
                ActorPersonnelNumber = number,
                ActorName = name,
                Action = entry.State switch
                {
                    EntityState.Added => AuditAction.Create,
                    EntityState.Deleted => AuditAction.Delete,
                    _ => AuditAction.Update,
                },
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = KeyOf(entry),
                ChangesJson = changes,
            });
        }
    }

    private static (AuditActorKind Kind, Guid? EmployeeId, string Number, string Name) Actor(HttpContext context, AuditPerson? person)
    {
        var user = context.User;
        if (user.FindFirst(AppClaimTypes.EmployeeId)?.Value is { } id && Guid.TryParse(id, out var employeeId))
        {
            return (AuditActorKind.Person, employeeId, person?.PersonnelNumber ?? string.Empty, person?.Name ?? string.Empty);
        }

        if (user.FindFirst(AppClaimTypes.ServiceName)?.Value is { Length: > 0 } service)
        {
            return (AuditActorKind.Service, null, string.Empty, service);
        }

        return (AuditActorKind.Anonymous, null, string.Empty, string.Empty);
    }

    private static string KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return string.Empty;
        }

        // A value the database still has to generate is unknown before the save.
        return string.Join(',', key.Properties.Select(p =>
            entry.Property(p.Name) is { IsTemporary: false, CurrentValue: { } v } ? Format(v) : string.Empty));
    }

    /// <summary>Created: all values; deleted: the values deleted; changed: only the properties that really changed (old and new), <c>null</c> if none.</summary>
    private static string? Changes(EntityEntry entry)
    {
        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (AuditOptions.IsSecret(name) || (property.Metadata.IsPrimaryKey() && entry.State != EntityState.Added))
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    if (property.CurrentValue is { } created)
                    {
                        values[name] = Format(created);
                    }

                    break;
                case EntityState.Deleted:
                    if (property.OriginalValue is { } removed)
                    {
                        values[name] = Format(removed);
                    }

                    break;
                default:
                    if (property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
                    {
                        values[name] = new { old = Format(property.OriginalValue), @new = Format(property.CurrentValue) };
                    }

                    break;
            }
        }

        return values.Count == 0 ? null : JsonSerializer.Serialize(values, Json);
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        byte[] bytes => $"[{bytes.Length} bytes]",
        string text => text.Length > MaxValueLength ? text[..MaxValueLength] + "…" : text,
        DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
