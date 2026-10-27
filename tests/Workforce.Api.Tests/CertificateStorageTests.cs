using Workforce.Infrastructure.Certificates;

namespace Workforce.Api.Tests;

/// <summary>
/// The separate file storage of the certificates: random names without extension, nothing but a Guid ever becomes a path, a file
/// appears whole or not at all, and the directory must lie outside the application.
/// </summary>
public sealed class CertificateStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "newworld-storage-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task A_file_is_stored_under_its_random_name_without_extension_and_read_back_unchanged()
    {
        var storage = new FileSystemCertificateStorage(_root);
        var id = Guid.NewGuid();

        await storage.WriteAsync(id, [1, 2, 3, 4], CancellationToken.None);

        Path.GetFileName(Directory.EnumerateFiles(_root).Single()).ShouldBe(id.ToString("N"));
        (await storage.ReadAsync(id, CancellationToken.None)).ShouldBe([1, 2, 3, 4]);
        (await storage.ReadAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_removes_the_file_and_a_missing_file_is_not_an_error()
    {
        var storage = new FileSystemCertificateStorage(_root);
        var id = Guid.NewGuid();
        await storage.WriteAsync(id, [1], CancellationToken.None);

        await storage.DeleteAsync(id, CancellationToken.None);
        await storage.DeleteAsync(id, CancellationToken.None);

        Directory.EnumerateFiles(_root).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_name_is_never_overwritten()
    {
        var storage = new FileSystemCertificateStorage(_root);
        var id = Guid.NewGuid();
        await storage.WriteAsync(id, [1], CancellationToken.None);

        await Should.ThrowAsync<IOException>(() => storage.WriteAsync(id, [2], CancellationToken.None));

        (await storage.ReadAsync(id, CancellationToken.None)).ShouldBe([1]);
        Directory.EnumerateFiles(_root).Count().ShouldBe(1); // the failed write left no partial file behind
    }

    [Fact]
    public async Task The_listing_shows_the_files_with_their_time_and_includes_unfinished_writes_so_they_can_be_swept()
    {
        var storage = new FileSystemCertificateStorage(_root);
        var finished = Guid.NewGuid();
        var unfinished = Guid.NewGuid();
        await storage.WriteAsync(finished, [1], CancellationToken.None);
        await File.WriteAllBytesAsync(Path.Combine(_root, unfinished.ToString("N") + ".part"), [9]);
        await File.WriteAllBytesAsync(Path.Combine(_root, "not-a-guid.txt"), [9]); // foreign files are ignored

        var files = await storage.ListAsync(CancellationToken.None);

        files.Select(f => f.StorageId).ShouldBe([finished, unfinished], ignoreOrder: true);
        files.ShouldAllBe(f => f.WrittenAt > DateTimeOffset.UtcNow.AddMinutes(-5));
        await storage.DeleteAsync(unfinished, CancellationToken.None);
        File.Exists(Path.Combine(_root, unfinished.ToString("N") + ".part")).ShouldBeFalse();
    }

    [Fact]
    public void The_directory_must_be_absolute_and_outside_the_application()
    {
        var app = Path.Combine(Path.GetTempPath(), "newworld-app");

        FileSystemCertificateStorage.ValidateRoot(Path.Combine(Path.GetTempPath(), "newworld-files"), app, "Setting")
            .ShouldBe(Path.Combine(Path.GetTempPath(), "newworld-files").TrimEnd(Path.DirectorySeparatorChar));
        Should.Throw<InvalidOperationException>(() => FileSystemCertificateStorage.ValidateRoot(null, app, "Setting"));
        Should.Throw<InvalidOperationException>(() => FileSystemCertificateStorage.ValidateRoot("relative/path", app, "Setting"));
        Should.Throw<InvalidOperationException>(() => FileSystemCertificateStorage.ValidateRoot(app, app, "Setting"));
        Should.Throw<InvalidOperationException>(() => FileSystemCertificateStorage.ValidateRoot(Path.Combine(app, "wwwroot", "files"), app, "Setting"))
            .Message.ShouldNotContain(app); // the message names the setting, not the paths
        // A directory next to the application with the same prefix is not inside it.
        FileSystemCertificateStorage.ValidateRoot(app + "-files", app, "Setting").ShouldBe(app + "-files");
    }
}
