using System.Text.RegularExpressions;

namespace Portal.Core.Faqs;

/// <summary>FAQ images: the format is recognised by the content (never by file name or the client's content type); links are found in the Markdown.</summary>
public static partial class FaqImageFormat
{
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>Number of bytes needed to recognise a format.</summary>
    public const int HeaderLength = 12;

    [GeneratedRegex("/api/faqs/images/([0-9a-fA-F-]{36})")]
    private static partial Regex ImageUrlRegex();

    /// <summary>Recognises PNG, JPEG and WebP by their first bytes; otherwise <c>null</c>.</summary>
    public static string? DetectExtension(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "png";
        }

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "jpg";
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return "webp";
        }

        return null;
    }

    public static string ContentType(string extension) => extension switch
    {
        "png" => "image/png",
        "jpg" => "image/jpeg",
        "webp" => "image/webp",
        _ => "application/octet-stream",
    };

    /// <summary>The ids of the images a Markdown text links to (only the link form <c>/api/faqs/images/{id}</c>, no Markdown parser).</summary>
    public static IReadOnlySet<Guid> ReferencedImages(string markdown) =>
        ImageUrlRegex().Matches(markdown)
            .Select(m => Guid.TryParse(m.Groups[1].Value, out var id) ? id : (Guid?)null)
            .OfType<Guid>()
            .ToHashSet();
}
