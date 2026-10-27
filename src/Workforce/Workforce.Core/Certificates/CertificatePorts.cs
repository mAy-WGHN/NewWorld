using Workforce.Core.Absences;

namespace Workforce.Core.Certificates;

/// <summary>
/// Where the (already encrypted) bytes of the certificates live: a storage that is separate from the database and from the application
/// files, addressed only by random names. It never sees a file name, a person or a date. Implementations must not interpret the bytes.
/// </summary>
public interface ICertificateFileStorage
{
    Task WriteAsync(Guid storageId, byte[] data, CancellationToken cancellationToken);

    /// <summary>The bytes, or <c>null</c> if there is no such file.</summary>
    Task<byte[]?> ReadAsync(Guid storageId, CancellationToken cancellationToken);

    /// <summary>Deletes the file; a missing file is not an error.</summary>
    Task DeleteAsync(Guid storageId, CancellationToken cancellationToken);

    /// <summary>All files with the time they were written (to find files no document refers to).</summary>
    Task<IReadOnlyList<(Guid StorageId, DateTimeOffset WrittenAt)>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Persistence of the certificates: metadata in the database, the content encrypted in the <see cref="ICertificateFileStorage"/>. The
/// content only ever leaves through <see cref="ReadContentAsync"/>. Changes to the metadata are saved by the caller (one unit of work
/// together with the absence and the outbox); files are written at once and deleted after the commit (or later by the orphan sweep).
/// </summary>
public interface ICertificateStore
{
    Task<IReadOnlyList<CertificateDocument>> ForAbsenceAsync(Guid absenceId, CancellationToken cancellationToken);

    Task<CertificateDocument?> GetAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>Adds the document and stores its content (encrypted by the store, in the separate file storage).</summary>
    Task AddAsync(CertificateDocument document, byte[] content, CancellationToken cancellationToken);

    /// <summary>The decrypted content, or <c>null</c> if it was deleted.</summary>
    Task<byte[]?> ReadContentAsync(CertificateDocument document, CancellationToken cancellationToken);

    /// <summary>Removes the content of a document; the metadata stays.</summary>
    Task RemoveContentAsync(CertificateDocument document, CancellationToken cancellationToken);

    /// <summary>Removes document and content (the uploader withdraws an upload, HR deletes).</summary>
    Task RemoveAsync(CertificateDocument document, CancellationToken cancellationToken);

    /// <summary>The retention period HR has set, or <c>null</c> if none was set (then the configured default applies).</summary>
    Task<CertificateRetention?> GetRetentionAsync(CancellationToken cancellationToken);

    /// <summary>Saves the retention period HR has set.</summary>
    Task SetRetentionAsync(CertificateRetention retention, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes files in the storage that no document with content refers to (left behind by a failed save or a deletion that did not finish)
    /// and that are older than <paramref name="writtenBefore"/> (so uploads in progress are left alone).
    /// </summary>
    Task<int> RemoveOrphanedFilesAsync(DateTimeOffset writtenBefore, CancellationToken cancellationToken);

    void Log(CertificateAccessEntry entry);

    /// <summary>Documents that still have content although the sick note ended on or before <paramref name="endedOnOrBefore"/>.</summary>
    Task<IReadOnlyList<CertificateDocument>> ExpiredAsync(DateOnly endedOnOrBefore, CancellationToken cancellationToken);

    /// <summary>Deletes access log entries older than <paramref name="olderThan"/>; returns how many.</summary>
    Task<int> PurgeLogAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);

    /// <summary>Sick notes without a usable certificate (missing or rejected) that may still be reminded.</summary>
    Task<IReadOnlyList<Absence>> WithoutCertificateAsync(int maxReminders, CancellationToken cancellationToken);
}

/// <summary>Checks an upload for malware before it is stored. A failed check counts as "not clean" (fail closed).</summary>
public interface IMalwareScanner
{
    Task<bool> IsCleanAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}
