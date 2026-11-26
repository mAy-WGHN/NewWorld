namespace Portal.Core.CashRegisters;

/// <summary>
/// Where the photos of the seals live (files, outside the web root, only reachable through their random id and the authorisation of the API). The
/// extension is part of the name so the content type follows from what is stored. The format check is the one of the other images of the Portal
/// (<c>FaqImageFormat</c>: recognised by the content, never by file name).
/// </summary>
public interface ICashRegisterPhotoStorage
{
    Task SaveAsync(Guid id, string extension, Stream content, CancellationToken cancellationToken);

    /// <returns>The content, or <c>null</c> if there is no such file.</returns>
    Stream? Open(Guid id, string extension);

    void Delete(Guid id, string extension);
}
