using Microsoft.EntityFrameworkCore;
using Workforce.Contracts;
using Workforce.Core.Absences;
using Workforce.Core.Certificates;

namespace Workforce.Infrastructure.Certificates;

/// <summary>The one row with the settings HR can change at runtime (the retention period of the files).</summary>
public sealed class CertificateSettingsRecord
{
    public int Id { get; set; } = 1;

    public int RetentionAfterEndDays { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public Guid ChangedBy { get; set; }
}

/// <summary>The one row with the reminder rhythm HR has set.</summary>
public sealed class CertificateReminderSettingsRecord
{
    public int Id { get; set; } = 1;

    public int FirstAfterReturnDays { get; set; }

    public int EveryDays { get; set; }

    public int MaxReminders { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public Guid ChangedBy { get; set; }
}

/// <summary>
/// Files to delete once the unit of work has been saved. Deleting before would lose the file if the save failed; deleting never would
/// leave files behind. What this misses (a crash in between) the orphan sweep of the maintenance service removes.
/// </summary>
public sealed class CertificateFileCleanup(ICertificateFileStorage storage)
{
    private readonly List<Guid> _pending = [];

    public void Add(Guid storageId) => _pending.Add(storageId);

    public void Discard() => _pending.Clear();

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var batch = _pending.ToArray();
        _pending.Clear();
        foreach (var storageId in batch)
        {
            try
            {
                await storage.DeleteAsync(storageId, cancellationToken);
            }
            catch (IOException)
            {
                // left for the orphan sweep; nothing to log that is not already an operational concern
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>
/// Metadata in the database, the encrypted bytes in the separate <see cref="ICertificateFileStorage"/> under a random name. The bytes
/// are encrypted here, before they reach the storage; the storage is addressed by <see cref="CertificateDocument.StorageId"/> only.
/// </summary>
public sealed class EfCertificateStore(
    WorkforceDbContext db, CertificateFileProtector protector, ICertificateFileStorage storage, CertificateFileCleanup cleanup) : ICertificateStore
{
    public async Task<IReadOnlyList<CertificateDocument>> ForAbsenceAsync(Guid absenceId, CancellationToken cancellationToken) =>
        await db.CertificateDocuments.Where(d => d.AbsenceId == absenceId).OrderBy(d => d.UploadedAt).ToListAsync(cancellationToken);

    public async Task<CertificateDocument?> GetAsync(Guid documentId, CancellationToken cancellationToken) =>
        await db.CertificateDocuments.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

    public async Task AddAsync(CertificateDocument document, byte[] content, CancellationToken cancellationToken)
    {
        // The file is written first; if the save fails afterwards the orphan sweep removes it.
        await storage.WriteAsync(document.StorageId, protector.Protect(content, document.Id), cancellationToken);
        db.CertificateDocuments.Add(document);
    }

    public async Task<byte[]?> ReadContentAsync(CertificateDocument document, CancellationToken cancellationToken) =>
        await storage.ReadAsync(document.StorageId, cancellationToken) is { } data ? protector.Unprotect(data, document.Id) : null;

    public Task RemoveContentAsync(CertificateDocument document, CancellationToken cancellationToken)
    {
        cleanup.Add(document.StorageId);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(CertificateDocument document, CancellationToken cancellationToken)
    {
        cleanup.Add(document.StorageId);
        db.CertificateDocuments.Remove(document);
        return Task.CompletedTask;
    }

    public async Task<CertificateRetention?> GetRetentionAsync(CancellationToken cancellationToken) =>
        await db.CertificateSettings.AsNoTracking().Where(s => s.Id == 1)
            .Select(s => new CertificateRetention(s.RetentionAfterEndDays, s.ChangedAt, s.ChangedBy))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SetRetentionAsync(CertificateRetention retention, CancellationToken cancellationToken)
    {
        var row = await db.CertificateSettings.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
        if (row is null)
        {
            row = new CertificateSettingsRecord();
            db.CertificateSettings.Add(row);
        }

        row.RetentionAfterEndDays = retention.DaysAfterEnd;
        row.ChangedAt = retention.ChangedAt;
        row.ChangedBy = retention.ChangedBy;
    }

    public async Task<CertificateReminderSettings?> GetReminderSettingsAsync(CancellationToken cancellationToken) =>
        await db.CertificateReminderSettings.AsNoTracking().Where(s => s.Id == 1)
            .Select(s => new CertificateReminderSettings(s.FirstAfterReturnDays, s.EveryDays, s.MaxReminders, s.ChangedAt, s.ChangedBy))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SetReminderSettingsAsync(CertificateReminderSettings settings, CancellationToken cancellationToken)
    {
        var row = await db.CertificateReminderSettings.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
        if (row is null)
        {
            row = new CertificateReminderSettingsRecord();
            db.CertificateReminderSettings.Add(row);
        }

        row.FirstAfterReturnDays = settings.FirstAfterReturnDays;
        row.EveryDays = settings.EveryDays;
        row.MaxReminders = settings.MaxReminders;
        row.ChangedAt = settings.ChangedAt;
        row.ChangedBy = settings.ChangedBy;
    }

    public async Task<int> RemoveOrphanedFilesAsync(DateTimeOffset writtenBefore, CancellationToken cancellationToken)
    {
        var candidates = (await storage.ListAsync(cancellationToken)).Where(f => f.WrittenAt < writtenBefore).Select(f => f.StorageId).ToList();
        var removed = 0;
        foreach (var batch in candidates.Chunk(500))
        {
            var ids = batch.ToList();
            var referenced = await db.CertificateDocuments.AsNoTracking()
                .Where(d => d.ContentDeletedAt == null && ids.Contains(d.StorageId))
                .Select(d => d.StorageId)
                .ToListAsync(cancellationToken);
            foreach (var storageId in ids.Except(referenced))
            {
                await storage.DeleteAsync(storageId, cancellationToken);
                removed++;
            }
        }

        return removed;
    }

    public void Log(CertificateAccessEntry entry) => db.CertificateAccessLog.Add(entry);

    public async Task<IReadOnlyList<CertificateDocument>> ExpiredAsync(DateOnly endedOnOrBefore, CancellationToken cancellationToken) =>
        await (from d in db.CertificateDocuments
               join a in db.Absences on d.AbsenceId equals a.Id
               where d.ContentDeletedAt == null && a.To != null && a.To <= endedOnOrBefore
               select d).ToListAsync(cancellationToken);

    public async Task<int> PurgeLogAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) =>
        await db.CertificateAccessLog.Where(e => e.At < olderThan).ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<Absence>> WithoutCertificateAsync(int maxReminders, CancellationToken cancellationToken) =>
        await db.Absences
            .Where(a => a.Type == AbsenceType.Sickness && a.Status == AbsenceStatus.Reported
                        && (a.CertificateStatus == CertificateStatus.Missing || a.CertificateStatus == CertificateStatus.Rejected)
                        && a.RemindersSent < maxReminders)
            .ToListAsync(cancellationToken);
}

/// <summary>
/// Placeholder for the malware check: accepts everything. Only used in Development/Testing without a configured scanner;
/// elsewhere the service refuses to start without <see cref="ClamAvOptions"/>. The type and size checks run before.
/// </summary>
public sealed class PassThroughMalwareScanner : IMalwareScanner
{
    public Task<bool> IsCleanAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken) => Task.FromResult(true);
}
