using Portal.Core.Faqs;

namespace Portal.Core.Tests;

public class FaqImageFormatTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Webp = [.. "RIFF"u8, 1, 2, 3, 4, .. "WEBP"u8];

    [Fact]
    public void PNG_JPEG_and_WebP_are_recognised_by_their_content()
    {
        FaqImageFormat.DetectExtension(Png).ShouldBe("png");
        FaqImageFormat.DetectExtension(Jpeg).ShouldBe("jpg");
        FaqImageFormat.DetectExtension(Webp).ShouldBe("webp");
    }

    [Theory]
    [InlineData("GIF89a......")]
    [InlineData("<svg xmlns=...")]
    [InlineData("%PDF-1.7 ....")]
    [InlineData("")]
    public void Everything_else_is_not_an_image_we_accept(string text) =>
        FaqImageFormat.DetectExtension(System.Text.Encoding.ASCII.GetBytes(text)).ShouldBeNull();

    [Fact]
    public void A_too_short_header_is_refused()
    {
        FaqImageFormat.DetectExtension(Png.AsSpan(0, 7)).ShouldBeNull();
        FaqImageFormat.DetectExtension(Webp.AsSpan(0, 11)).ShouldBeNull();
    }

    [Fact]
    public void The_content_type_follows_the_detected_extension()
    {
        FaqImageFormat.ContentType("png").ShouldBe("image/png");
        FaqImageFormat.ContentType("jpg").ShouldBe("image/jpeg");
        FaqImageFormat.ContentType("webp").ShouldBe("image/webp");
        FaqImageFormat.ContentType("exe").ShouldBe("application/octet-stream");
    }

    [Fact]
    public void Linked_images_are_found_in_the_markdown_and_other_text_is_ignored()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var markdown = $"Text ![x](https://host/api/faqs/images/{a}) und ![y](/api/faqs/images/{b}) sowie /api/faqs/images/not-a-guid-at-all-not-a-guid-12345 und ![z](https://other/img.png)";

        FaqImageFormat.ReferencedImages(markdown).ShouldBe([a, b], ignoreOrder: true);
        FaqImageFormat.ReferencedImages("").ShouldBeEmpty();
    }

    [Fact]
    public void The_same_image_linked_twice_counts_once()
    {
        var a = Guid.NewGuid();

        FaqImageFormat.ReferencedImages($"/api/faqs/images/{a} /api/faqs/images/{a}").Count.ShouldBe(1);
    }
}
