using Workforce.Core.Absences;
using Workforce.Core.Certificates;

namespace Workforce.Core.Tests;

internal sealed class FakeCertificateStore(FakeStore absences) : ICertificateStore
{
    public List<CertificateDocument> Documents { get; } = [];
    public Dictionary<Guid, byte[]> Contents { get; } = [];
    public List<CertificateAccessEntry> LogEntries { get; } = [];
    public int PurgedLogCalls { get; private set; }
    public CertificateRetention? Retention { get; set; }
    public List<DateTimeOffset> OrphanSweeps { get; } = [];

    public Task<IReadOnlyList<CertificateDocument>> ForAbsenceAsync(Guid absenceId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CertificateDocument>>(Documents.Where(d => d.AbsenceId == absenceId).ToList());

    public Task<CertificateDocument?> GetAsync(Guid documentId, CancellationToken cancellationToken) =>
        Task.FromResult(Documents.FirstOrDefault(d => d.Id == documentId));

    public Task AddAsync(CertificateDocument document, byte[] content, CancellationToken cancellationToken)
    {
        Documents.Add(document);
        Contents[document.Id] = content;
        return Task.CompletedTask;
    }

    public Task<byte[]?> ReadContentAsync(CertificateDocument document, CancellationToken cancellationToken) =>
        Task.FromResult(Contents.TryGetValue(document.Id, out var bytes) ? bytes : null);

    public Task RemoveContentAsync(CertificateDocument document, CancellationToken cancellationToken)
    {
        Contents.Remove(document.Id);
        return Task.CompletedTask;
    }

    public Task<CertificateRetention?> GetRetentionAsync(CancellationToken cancellationToken) => Task.FromResult(Retention);

    public Task SetRetentionAsync(CertificateRetention retention, CancellationToken cancellationToken)
    {
        Retention = retention;
        return Task.CompletedTask;
    }

    public Task<int> RemoveOrphanedFilesAsync(DateTimeOffset writtenBefore, CancellationToken cancellationToken)
    {
        OrphanSweeps.Add(writtenBefore);
        return Task.FromResult(0);
    }

    public Task RemoveAsync(CertificateDocument document, CancellationToken cancellationToken)
    {
        Documents.Remove(document);
        Contents.Remove(document.Id);
        return Task.CompletedTask;
    }

    public void Log(CertificateAccessEntry entry) => LogEntries.Add(entry);

    public Task<IReadOnlyList<CertificateDocument>> ExpiredAsync(DateOnly endedOnOrBefore, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CertificateDocument>>(Documents
            .Where(d => d.HasContent && absences.Absences.Any(a => a.Id == d.AbsenceId && a.To is { } end && end <= endedOnOrBefore))
            .ToList());

    public Task<int> PurgeLogAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        PurgedLogCalls++;
        return Task.FromResult(LogEntries.RemoveAll(e => e.At < olderThan));
    }

    public Task<IReadOnlyList<Absence>> WithoutCertificateAsync(int maxReminders, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Absence>>(absences.Absences
            .Where(a => a is { Type: Workforce.Contracts.AbsenceType.Sickness, Status: Workforce.Contracts.AbsenceStatus.Reported }
                        && a.CertificateStatus is CertificateStatus.Missing or CertificateStatus.Rejected && a.RemindersSent < maxReminders)
            .ToList());
}

internal sealed class FakeScanner(bool clean = true) : IMalwareScanner
{
    public int Calls { get; private set; }

    public Task<bool> IsCleanAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(clean);
    }
}
