using System.IO.Compression;

namespace Portal.Core.Faqs;

/// <summary>What an attached file turned out to be: the extension that is stored and the content type that is sent back.</summary>
public sealed record FaqAttachmentKind(string Extension, string ContentType, bool IsImage, bool OpensInBrowser);

/// <summary>
/// Files attached to a FAQ entry: PDF, images and Office documents. The type is recognised from the content (the file name and the content type of
/// the client are never trusted; the name is only used to tell the Office formats of the same container apart). Anything that can run or carry a
/// script (HTML, SVG, scripts, executables, macro documents) is refused.
/// </summary>
public static class FaqAttachmentFormat
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public const int MaxPerEntry = 20;

    /// <summary>The first bytes are enough to tell the families apart.</summary>
    public const int HeaderLength = 12;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Ole2 = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>The families are recognised by the header; the Office formats by the extension of the name (inside their container).</summary>
    public static FaqAttachmentKind? Detect(ReadOnlySpan<byte> header, string fileName, Stream content)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();

        if (header.Length >= 5 && header[..5].SequenceEqual("%PDF-"u8))
        {
            return extension == "pdf" || extension.Length == 0 ? Of("pdf") : null;
        }

        if (header.Length >= 8 && header[..8].SequenceEqual(Png))
        {
            return Of("png");
        }

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return Of("jpg");
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return Of("webp");
        }

        // Old Office formats (one OLE2 container): only the extension tells word from excel from powerpoint.
        if (header.Length >= 8 && header[..8].SequenceEqual(Ole2))
        {
            return extension is "doc" or "xls" or "ppt" ? Of(extension) : null;
        }

        // New Office formats (a zip): the folders inside tell which one it is, macros are not accepted.
        if (header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] is 0x03 or 0x05 && header[3] is 0x04 or 0x06)
        {
            return OfficeOpenXml(extension, content);
        }

        return null;
    }

    private static FaqAttachmentKind? OfficeOpenXml(string extension, Stream content)
    {
        var folder = extension switch { "docx" => "word/", "xlsx" => "xl/", "pptx" => "ppt/", _ => null };
        if (folder is null)
        {
            return null;
        }

        try
        {
            content.Position = 0;
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            var macros = names.Any(n => n.Contains("vbaProject", StringComparison.OrdinalIgnoreCase));
            return !macros && names.Contains("[Content_Types].xml") && names.Any(n => n.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) ? Of(extension) : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        finally
        {
            content.Position = 0;
        }
    }

    private static FaqAttachmentKind Of(string extension) => extension switch
    {
        "pdf" => new(extension, "application/pdf", false, true),
        "png" => new(extension, "image/png", true, true),
        "jpg" => new(extension, "image/jpeg", true, true),
        "webp" => new(extension, "image/webp", true, true),
        "docx" => new(extension, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", false, false),
        "xlsx" => new(extension, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false, false),
        "pptx" => new(extension, "application/vnd.openxmlformats-officedocument.presentationml.presentation", false, false),
        "doc" => new(extension, "application/msword", false, false),
        "xls" => new(extension, "application/vnd.ms-excel", false, false),
        _ => new("ppt", "application/vnd.ms-powerpoint", false, false),
    };

    /// <summary>The content type for a stored extension.</summary>
    public static string ContentType(string extension) => Of(extension).ContentType;

    public static bool IsImage(string extension) => extension is "png" or "jpg" or "webp";

    /// <summary>The name as it is shown and offered for download: no path, no control characters, not empty, not too long.</summary>
    public static string CleanName(string? fileName, string extension)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '|' or ':' or '*' or '?')).ToArray()).Trim();
        if (name.Length == 0 || name == "." || name == "..")
        {
            name = "Anhang";
        }

        if (!name.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase) && !(extension == "jpg" && (name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))))
        {
            name = $"{name}.{extension}";
        }

        return name.Length > 255 ? name[^255..] : name;
    }
}
