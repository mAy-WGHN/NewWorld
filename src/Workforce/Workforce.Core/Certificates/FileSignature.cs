namespace Workforce.Core.Certificates;

/// <summary>
/// Recognises the file type from the first bytes (the file name and the declared content type are never trusted). Only PDF, JPEG and PNG
/// are accepted, which is what a scanned or photographed certificate is.
/// </summary>
public static class FileSignature
{
    public const string Pdf = "application/pdf";
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";

    /// <summary>The content type, or <c>null</c> if the content is none of the accepted types.</summary>
    public static string? Detect(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 5 && content[..5].SequenceEqual("%PDF-"u8))
        {
            return Pdf;
        }

        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return Jpeg;
        }

        if (content.Length >= 8 && content[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return Png;
        }

        return null;
    }

    /// <summary>File extension for the neutral download name.</summary>
    public static string Extension(string contentType) => contentType switch
    {
        Pdf => "pdf",
        Jpeg => "jpg",
        Png => "png",
        _ => "bin",
    };
}
