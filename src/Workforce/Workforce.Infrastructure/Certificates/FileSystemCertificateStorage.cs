using System.Globalization;
using Workforce.Core.Certificates;

namespace Workforce.Infrastructure.Certificates;

/// <summary>
/// The separate file storage of the certificates: one directory outside the application files (never below the content root, never
/// served by the web server) that only this service may read and write. Every file is named by a random UUID without extension, so a
/// stored file can never be executed or interpreted by its name; person, date and type are only in the database. The bytes are already
/// encrypted when they arrive. The storage never builds a path from anything but a <see cref="Guid"/>.
/// For Azure hosting the same port can be served by a blob container with its own identity; the decision is open (hosting).
/// </summary>
public sealed class FileSystemCertificateStorage : ICertificateFileStorage
{
    private const string TemporarySuffix = ".part";
    private readonly string _root;

    /// <summary>The directory of the files (for operations and tests; never part of a response).</summary>
    public string Root => _root;

    public FileSystemCertificateStorage(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// The directory from configuration. It must be an absolute path and must not lie inside the application directory (content root):
    /// what the application serves or deploys must never contain the files. The error names the setting only.
    /// </summary>
    public static string ValidateRoot(string? path, string contentRoot, string setting)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException($"{setting} must be an absolute path to a directory only this service can access.");
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var content = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRoot));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (full.Equals(content, comparison) || full.StartsWith(content + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException($"{setting} must not be inside the application directory.");
        }

        return full;
    }

    public async Task WriteAsync(Guid storageId, byte[] data, CancellationToken cancellationToken)
    {
        var target = PathOf(storageId);
        var temporary = target + TemporarySuffix;
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using (var stream = new FileStream(temporary, options))
        {
            await stream.WriteAsync(data, cancellationToken);
        }

        try
        {
            File.Move(temporary, target, overwrite: false); // a file appears whole or not at all, and a name is never reused
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    public async Task<byte[]?> ReadAsync(Guid storageId, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(PathOf(storageId), cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public Task DeleteAsync(Guid storageId, CancellationToken cancellationToken)
    {
        var path = PathOf(storageId);
        File.Delete(path); // no error if it is not there
        File.Delete(path + TemporarySuffix);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(Guid StorageId, DateTimeOffset WrittenAt)>> ListAsync(CancellationToken cancellationToken)
    {
        var files = new List<(Guid, DateTimeOffset)>();
        foreach (var path in Directory.EnumerateFiles(_root))
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith(TemporarySuffix, StringComparison.Ordinal))
            {
                name = name[..^TemporarySuffix.Length]; // an unfinished write that never completed is an orphan like any other
            }

            if (Guid.TryParseExact(name, "N", out var id))
            {
                files.Add((id, new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)));
            }
        }

        return Task.FromResult<IReadOnlyList<(Guid, DateTimeOffset)>>(files);
    }

    private string PathOf(Guid storageId) => Path.Combine(_root, storageId.ToString("N", CultureInfo.InvariantCulture));
}
