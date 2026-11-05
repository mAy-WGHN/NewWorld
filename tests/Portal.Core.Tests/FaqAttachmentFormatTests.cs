using System.IO.Compression;
using System.Text;
using Portal.Core.Faqs;

namespace Portal.Core.Tests;

public class FaqAttachmentFormatTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7 content");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Webp = [.. "RIFF"u8, 1, 2, 3, 4, .. "WEBP"u8];
    private static readonly byte[] Ole2 = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0];

    private static readonly string[] Images = ["png", "jpg", "webp"];
    private static readonly string[] NoImages = ["pdf", "docx", "xls"];

    private static byte[] Zip(params string[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write("x"u8);
            }
        }

        return stream.ToArray();
    }

    private static FaqAttachmentKind? Detect(byte[] content, string fileName)
    {
        using var stream = new MemoryStream(content);
        return FaqAttachmentFormat.Detect(content.AsSpan(0, Math.Min(content.Length, FaqAttachmentFormat.HeaderLength)), fileName, stream);
    }

    [Fact]
    public void PDF_and_the_images_are_recognised_by_their_content()
    {
        Detect(Pdf, "a.pdf")!.ShouldBe(new FaqAttachmentKind("pdf", "application/pdf", false, true));
        Detect(Png, "a.png")!.ShouldBe(new FaqAttachmentKind("png", "image/png", true, true));
        Detect(Jpeg, "a.jpeg")!.Extension.ShouldBe("jpg");
        Detect(Webp, "a.webp")!.ContentType.ShouldBe("image/webp");
    }

    [Fact]
    public void The_name_and_the_type_of_the_client_do_not_decide_for_PDF_and_images()
    {
        Detect(Png, "tarnung.pdf")!.Extension.ShouldBe("png"); // the content wins
        Detect(Pdf, "ohne-endung")!.Extension.ShouldBe("pdf");
        Detect(Pdf, "a.exe").ShouldBeNull(); // a PDF with the name of a program is not taken
    }

    [Theory]
    [InlineData("doc", "application/msword")]
    [InlineData("xls", "application/vnd.ms-excel")]
    [InlineData("ppt", "application/vnd.ms-powerpoint")]
    public void Old_Office_files_are_taken_by_their_container_and_their_ending(string extension, string contentType)
    {
        var kind = Detect(Ole2, $"datei.{extension}")!;

        kind.Extension.ShouldBe(extension);
        kind.ContentType.ShouldBe(contentType);
        kind.IsImage.ShouldBeFalse();
        kind.OpensInBrowser.ShouldBeFalse();
    }

    [Fact]
    public void An_old_Office_container_with_another_ending_is_refused()
    {
        Detect(Ole2, "datei.exe").ShouldBeNull();
        Detect(Ole2, "datei.msi").ShouldBeNull(); // msi installers are OLE containers, too
        Detect(Ole2, "datei").ShouldBeNull();
    }

    [Theory]
    [InlineData("docx", "word/document.xml")]
    [InlineData("xlsx", "xl/workbook.xml")]
    [InlineData("pptx", "ppt/presentation.xml")]
    public void New_Office_files_are_taken_if_the_folders_inside_fit_the_ending(string extension, string part)
    {
        var content = Zip("[Content_Types].xml", part);

        Detect(content, $"datei.{extension}")!.Extension.ShouldBe(extension);
    }

    [Fact]
    public void A_zip_that_is_no_Office_document_is_refused()
    {
        Detect(Zip("a.txt", "b.txt"), "daten.docx").ShouldBeNull(); // no Office parts
        Detect(Zip("[Content_Types].xml", "word/document.xml"), "daten.xlsx").ShouldBeNull(); // the folders do not fit the ending
        Detect(Zip("[Content_Types].xml", "word/document.xml"), "daten.zip").ShouldBeNull();
        Detect(Zip("[Content_Types].xml", "word/document.xml"), "daten.docm").ShouldBeNull();
    }

    [Fact]
    public void An_Office_file_with_macros_is_refused()
    {
        Detect(Zip("[Content_Types].xml", "word/document.xml", "word/vbaProject.bin"), "makro.docx").ShouldBeNull();
    }

    [Fact]
    public void A_broken_zip_is_refused_and_the_stream_is_left_at_the_start()
    {
        var broken = new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        using var stream = new MemoryStream(broken);

        FaqAttachmentFormat.Detect(broken.AsSpan(0, 12), "kaputt.docx", stream).ShouldBeNull();

        stream.Position.ShouldBe(0);
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script>", "seite.html")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script/></svg>", "bild.svg")]
    [InlineData("MZ\u0090\u0000\u0003", "programm.exe")]
    [InlineData("#!/bin/sh\nrm -rf /", "skript.sh")]
    [InlineData("GIF89a......", "bild.gif")]
    [InlineData("plain text", "notiz.txt")]
    [InlineData("", "leer.pdf")]
    public void Everything_that_can_run_or_carry_a_script_and_everything_unknown_is_refused(string text, string fileName) =>
        Detect(Encoding.Latin1.GetBytes(text), fileName).ShouldBeNull();

    [Theory]
    [InlineData("Bericht.pdf", "pdf", "Bericht.pdf")]
    [InlineData("Bericht", "pdf", "Bericht.pdf")]
    [InlineData("Foto.JPEG", "jpg", "Foto.JPEG")]
    [InlineData("..\\..\\evil.pdf", "pdf", "evil.pdf")]
    [InlineData("../../etc/passwd", "pdf", "passwd.pdf")]
    [InlineData("a<b>c|d:e*f?.pdf", "pdf", "abcdef.pdf")]
    [InlineData("tab\tund\nzeile.pdf", "pdf", "tabundzeile.pdf")]
    [InlineData("", "pdf", "Anhang.pdf")]
    [InlineData("..", "pdf", "Anhang.pdf")]
    [InlineData(null, "docx", "Anhang.docx")]
    public void The_name_is_cleaned_for_showing_and_downloading(string? name, string extension, string expected) =>
        FaqAttachmentFormat.CleanName(name, extension).ShouldBe(expected);

    [Fact]
    public void A_very_long_name_is_cut_to_what_the_database_holds()
    {
        FaqAttachmentFormat.CleanName(new string('a', 400) + ".pdf", "pdf").Length.ShouldBeLessThanOrEqualTo(255);
    }

    [Fact]
    public void The_limits_are_ten_megabytes_and_twenty_files()
    {
        FaqAttachmentFormat.MaxBytes.ShouldBe(10 * 1024 * 1024);
        FaqAttachmentFormat.MaxPerEntry.ShouldBe(20);
    }

    [Fact]
    public void Only_the_image_extensions_are_images()
    {
        Images.ShouldAllBe(e => FaqAttachmentFormat.IsImage(e));
        NoImages.ShouldAllBe(e => !FaqAttachmentFormat.IsImage(e));
    }
}
