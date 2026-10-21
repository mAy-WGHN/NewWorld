namespace Portal.Services;

/// <summary>
/// Ablage der FAQ-Bilder im Dateisystem (außerhalb des Webroots, nur per unratbarer UUID abrufbar).
/// Das Format wird am Inhalt erkannt, nicht am Dateinamen oder Content-Type des Clients.
/// </summary>
public class FaqImageStorage(IConfiguration configuration, IWebHostEnvironment env)
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private string Directory => Path.GetFullPath(
        configuration["Uploads:FaqPath"] is { Length: > 0 } path ? path : "var/uploads/faq",
        env.ContentRootPath);

    public string PathFor(Guid id, string extension) => Path.Combine(Directory, $"{id}.{extension}");

    public async Task SaveAsync(Guid id, string extension, Stream content, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Directory);
        await using var target = File.Create(PathFor(id, extension));
        await content.CopyToAsync(target, ct);
    }

    public void Delete(Guid id, string extension)
    {
        var path = PathFor(id, extension);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Erkennt PNG, JPEG und WebP an den Magic Bytes; sonst null.</summary>
    public static string? DetectExtension(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "png";
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return "jpg";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return "webp";
        return null;
    }

    public static string ContentType(string extension) => extension switch
    {
        "png" => "image/png",
        "jpg" => "image/jpeg",
        "webp" => "image/webp",
        _ => "application/octet-stream"
    };
}
