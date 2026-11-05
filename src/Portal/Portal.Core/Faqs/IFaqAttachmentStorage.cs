namespace Portal.Core.Faqs;

/// <summary>
/// Where the files attached to FAQ entries live (files, outside the web root, only reachable through the API after the sign-in). The stored
/// name is the random id plus the extension; the name the person gave is kept in the database only.
/// </summary>
public interface IFaqAttachmentStorage
{
    Task SaveAsync(Guid id, string extension, Stream content, CancellationToken cancellationToken);

    /// <returns>The content, or <c>null</c> if there is no such file.</returns>
    Stream? Open(Guid id, string extension);

    void Delete(Guid id, string extension);
}
