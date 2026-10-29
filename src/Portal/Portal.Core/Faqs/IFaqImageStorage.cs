namespace Portal.Core.Faqs;

/// <summary>
/// Where the FAQ images live (files, outside the web root, only reachable through their random id). The extension is part of the name so
/// the content type follows from what is stored.
/// </summary>
public interface IFaqImageStorage
{
    Task SaveAsync(Guid id, string extension, Stream content, CancellationToken cancellationToken);

    /// <returns>The content, or <c>null</c> if there is no such file.</returns>
    Stream? Open(Guid id, string extension);

    void Delete(Guid id, string extension);
}
