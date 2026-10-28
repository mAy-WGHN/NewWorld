using Microsoft.EntityFrameworkCore;
using Notifications.Core.Templates;

namespace Notifications.Infrastructure.Delivery;

/// <summary>Read-only copy of the name of an employee (from <c>EmployeeChanged</c>), for <c>$USER.firstName</c>. Organisation data (class 1).</summary>
public sealed class EmployeeNameRecord
{
    public Guid EmployeeId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public DateOnly? ExitDate { get; set; }

    /// <summary>Time of the message this state comes from; older messages are ignored.</summary>
    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>The texts HR changed for one template; a column that is <c>null</c> follows the file.</summary>
public sealed class TemplateOverrideRecord
{
    public required string TemplateKey { get; set; }
    public string? InAppTitle { get; set; }
    public string? InAppBody { get; set; }
    public string? PushTitle { get; set; }
    public string? PushBody { get; set; }
    public string? EmailTitle { get; set; }
    public string? EmailBody { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public Guid ChangedBy { get; set; }
}

public sealed class EfRecipientDirectory(NotificationsDbContext db) : IRecipientDirectory
{
    public async Task<IReadOnlyDictionary<Guid, RecipientName>> NamesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken) =>
        (await db.EmployeeNames.AsNoTracking().Where(n => employeeIds.Contains(n.EmployeeId)).ToListAsync(cancellationToken))
            .ToDictionary(n => n.EmployeeId, n => new RecipientName(n.FirstName, n.LastName));
}

public sealed class EfTemplateOverrides(NotificationsDbContext db) : ITemplateOverrides
{
    public async Task<TemplateOverride?> GetAsync(string key, CancellationToken cancellationToken) =>
        await db.TemplateOverrides.AsNoTracking().FirstOrDefaultAsync(o => o.TemplateKey == key, cancellationToken) is { } row ? ToOverride(row) : null;

    public async Task<IReadOnlyList<TemplateOverride>> ListAsync(CancellationToken cancellationToken) =>
        [.. (await db.TemplateOverrides.AsNoTracking().ToListAsync(cancellationToken)).Select(ToOverride)];

    public async Task SaveAsync(TemplateOverride change, CancellationToken cancellationToken)
    {
        var row = await db.TemplateOverrides.FindAsync([change.Key], cancellationToken);
        if (row is null)
        {
            row = new TemplateOverrideRecord { TemplateKey = change.Key };
            db.TemplateOverrides.Add(row);
        }

        row.InAppTitle = change.Texts.InApp?.Title;
        row.InAppBody = change.Texts.InApp?.Body;
        row.PushTitle = change.Texts.Push?.Title;
        row.PushBody = change.Texts.Push?.Body;
        row.EmailTitle = change.Texts.Email?.Title;
        row.EmailBody = change.Texts.Email?.Body;
        row.ChangedAt = change.ChangedAt;
        row.ChangedBy = change.ChangedBy;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var deleted = await db.TemplateOverrides.Where(o => o.TemplateKey == key).ExecuteDeleteAsync(cancellationToken) > 0;
        return deleted;
    }

    private static TemplateOverride ToOverride(TemplateOverrideRecord r) => new(
        r.TemplateKey,
        new TemplateTexts(Pair(r.InAppTitle, r.InAppBody), Pair(r.PushTitle, r.PushBody), Pair(r.EmailTitle, r.EmailBody)),
        r.ChangedAt, r.ChangedBy);

    private static TextPair? Pair(string? title, string? body) => title is null || body is null ? null : new TextPair(title, body);
}
