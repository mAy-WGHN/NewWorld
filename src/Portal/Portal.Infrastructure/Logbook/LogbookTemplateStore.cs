using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Portal.Core.Logbook;

namespace Portal.Infrastructure.Logbook;

/// <summary>One saved version of the template. The sections are one JSON document (they are always read and written as a whole); the version is the key.</summary>
public sealed class LogbookTemplateRecord
{
    public int Version { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public Guid PublishedBy { get; set; }
    public required string SectionsJson { get; set; }
}

/// <summary>Raised when another change took the version in the meantime (two people saved at the same time).</summary>
public sealed class LogbookTemplateConflictException : Exception;

/// <summary>
/// The versions of the template. Until the first version is saved the suggestion (<see cref="LogbookTemplate.Default"/>, version 0) applies. A change of the
/// content makes the next version; the same content makes none. Old versions stay, reports keep the one they were filled in with.
/// </summary>
public sealed class LogbookTemplateStore(PortalDbContext db)
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public async Task<LogbookTemplate> CurrentAsync(CancellationToken cancellationToken)
    {
        var latest = await db.LogbookTemplates.AsNoTracking().OrderByDescending(t => t.Version).FirstOrDefaultAsync(cancellationToken);
        return latest is null ? LogbookTemplate.Default : ToTemplate(latest);
    }

    /// <summary>The version, or <c>null</c> if there is none. Version 0 is the suggestion.</summary>
    public async Task<LogbookTemplate?> GetAsync(int version, CancellationToken cancellationToken)
    {
        if (version == 0)
        {
            return LogbookTemplate.Default;
        }

        var record = await db.LogbookTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Version == version, cancellationToken);
        return record is null ? null : ToTemplate(record);
    }

    /// <summary>Saves the sections as the next version unless they are the same as in the current one; returns the template that now applies.</summary>
    /// <exception cref="LogbookTemplateConflictException">Somebody else took the version at the same time.</exception>
    public async Task<LogbookTemplate> PublishAsync(IReadOnlyList<LogbookSection> sections, Guid by, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = await CurrentAsync(cancellationToken);
        if (current.Version > 0 && current.HasSameContent(sections))
        {
            return current;
        }

        var record = new LogbookTemplateRecord
        {
            Version = current.Version + 1,
            PublishedAt = now,
            PublishedBy = by,
            SectionsJson = JsonSerializer.Serialize(sections.Select(Clean).ToList(), Json),
        };
        db.LogbookTemplates.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.Entry(record).State = EntityState.Detached;
            if (await db.LogbookTemplates.AnyAsync(t => t.Version == record.Version, cancellationToken))
            {
                throw new LogbookTemplateConflictException();
            }

            throw;
        }

        return ToTemplate(record);
    }

    /// <summary>Spaces around titles, hints and labels are not kept.</summary>
    private static LogbookSection Clean(LogbookSection s) => s with
    {
        Title = s.Title.Trim(),
        Hint = string.IsNullOrWhiteSpace(s.Hint) ? null : s.Hint.Trim(),
        Options = [.. s.Options.Select(o => o with { Label = o.Label.Trim() })],
    };

    private static LogbookTemplate ToTemplate(LogbookTemplateRecord record) =>
        new(record.Version, JsonSerializer.Deserialize<List<LogbookSection>>(record.SectionsJson, Json) ?? [], record.PublishedAt, record.PublishedBy);
}
