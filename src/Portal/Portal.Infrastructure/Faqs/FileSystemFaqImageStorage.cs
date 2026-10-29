using Portal.Core.Faqs;

namespace Portal.Infrastructure.Faqs;

/// <summary>Images as files in one folder (<c>Portal:Faq:ImagePath</c>, outside the application folder), named by id and extension.</summary>
public sealed class FileSystemFaqImageStorage(string root) : IFaqImageStorage
{
    private string PathFor(Guid id, string extension) => Path.Combine(root, $"{id}.{extension}");

    public async Task SaveAsync(Guid id, string extension, Stream content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        await using var target = File.Create(PathFor(id, extension));
        await content.CopyToAsync(target, cancellationToken);
    }

    public Stream? Open(Guid id, string extension)
    {
        var path = PathFor(id, extension);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }

    public void Delete(Guid id, string extension)
    {
        var path = PathFor(id, extension);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
