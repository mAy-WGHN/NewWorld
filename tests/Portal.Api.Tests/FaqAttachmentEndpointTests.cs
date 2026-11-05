using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Portal.Core;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// Files attached to a FAQ entry through the real API and database: who may attach and remove, that only signed-in people can see and fetch them,
/// what is accepted (checked on the content), the limits, and that the files go with the entry.
/// </summary>
public sealed class FaqAttachmentEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7 the content of the file");

    private static byte[] Docx()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "[Content_Types].xml", "word/document.xml" })
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write("x"u8);
            }
        }

        return stream.ToArray();
    }

    private static async Task<Guid> CreateEntryAsync(PortalApi api, string title = "Eintrag")
    {
        using var response = await api.SendAsync(HttpMethod.Post, "/api/faqs", Hanna, new { title, description = "Antwort" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await DataAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> AttachAsync(PortalApi api, Guid faqId, Guid? person, byte[] content, string fileName = "datei.pdf")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/faqs/{faqId}/attachments");
        if (person is { } p)
        {
            request.AsUser(p);
        }

        request.Content = new MultipartFormDataContent { { new ByteArrayContent(content), "file", fileName } };
        return await api.Http.SendAsync(request);
    }

    private static async Task<JsonElement> AttachOkAsync(PortalApi api, Guid faqId, byte[] content, string fileName = "datei.pdf")
    {
        using var response = await AttachAsync(api, faqId, Hanna, content, fileName);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private static async Task<JsonElement> EntryAsync(PortalApi api, Guid faqId, Guid? person)
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/api/faqs", person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await DataAsync(response)).EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == faqId);
    }

    private static string[] StoredFiles(PortalApi api) =>
        Directory.Exists(api.AttachmentFolder) ? Directory.GetFiles(api.AttachmentFolder) : [];

    // ------------------------------------------------------------------ attaching and listing

    [Fact]
    public async Task A_file_is_attached_and_listed_with_its_name_type_size_and_address()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);

        var attachment = await AttachOkAsync(api, faq, Pdf, "Urlaubsantrag.pdf");

        attachment.GetProperty("fileName").GetString().ShouldBe("Urlaubsantrag.pdf");
        attachment.GetProperty("contentType").GetString().ShouldBe("application/pdf");
        attachment.GetProperty("length").GetInt64().ShouldBe(Pdf.Length);
        attachment.GetProperty("isImage").GetBoolean().ShouldBeFalse();
        attachment.GetProperty("url").GetString().ShouldBe($"/api/faqs/{faq}/attachments/{attachment.GetProperty("id").GetGuid()}");
        StoredFiles(api).Length.ShouldBe(1);
        var listed = (await EntryAsync(api, faq, Erika)).GetProperty("attachments").EnumerateArray().Single();
        listed.GetProperty("id").GetGuid().ShouldBe(attachment.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task The_entry_is_public_but_its_files_are_listed_only_for_signed_in_people()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);
        await AttachOkAsync(api, faq, Pdf);

        (await EntryAsync(api, faq, person: null)).GetProperty("attachments").GetArrayLength().ShouldBe(0);
        (await EntryAsync(api, faq, Erika)).GetProperty("attachments").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Images_and_Office_documents_are_accepted_and_images_are_marked()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);

        var image = await AttachOkAsync(api, faq, Png, "foto.png");
        var word = await AttachOkAsync(api, faq, Docx(), "Anleitung.docx");

        image.GetProperty("isImage").GetBoolean().ShouldBeTrue();
        image.GetProperty("contentType").GetString().ShouldBe("image/png");
        word.GetProperty("isImage").GetBoolean().ShouldBeFalse();
        word.GetProperty("contentType").GetString().ShouldBe("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }

    [Fact]
    public async Task The_name_is_cleaned_and_the_ending_follows_the_content()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);

        (await AttachOkAsync(api, faq, Pdf, "..\\..\\geheim.pdf")).GetProperty("fileName").GetString().ShouldBe("geheim.pdf");
        (await AttachOkAsync(api, faq, Png, "bild.pdf")).GetProperty("fileName").GetString().ShouldBe("bild.pdf.png"); // the content is a PNG
    }

    // ------------------------------------------------------------------ who may attach and remove

    [Fact]
    public async Task Attaching_and_removing_need_the_privilege_to_change_entries()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);
        var attachment = await AttachOkAsync(api, faq, Pdf);
        var url = attachment.GetProperty("url").GetString()!;

        (await AttachAsync(api, faq, Erika, Pdf)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AttachAsync(api, faq, person: null, Pdf)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.SendAsync(HttpMethod.Delete, url, Erika)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Delete, url, person: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        api.Grant(Ben, TestGrant.Everywhere(PortalPrivileges.FaqRead), TestGrant.Everywhere(PortalPrivileges.FaqCreate));
        (await AttachAsync(api, faq, Ben, Pdf)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // reading and creating is not changing
        StoredFiles(api).Length.ShouldBe(1);
    }

    [Fact]
    public async Task A_file_cannot_be_attached_to_an_entry_that_does_not_exist()
    {
        await using var api = await StartAsync(postgres);

        (await AttachAsync(api, Guid.NewGuid(), Hanna, Pdf)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        StoredFiles(api).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ fetching

    [Fact]
    public async Task A_signed_in_person_fetches_the_file_and_gets_it_unchanged_without_anything_the_browser_could_run()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);
        var url = (await AttachOkAsync(api, faq, Pdf, "Formular.pdf")).GetProperty("url").GetString()!;

        using var response = await api.SendAsync(HttpMethod.Get, url, Erika);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(Pdf);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("sandbox");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("inline"); // PDF and images open in the tab
        response.Content.Headers.ContentDisposition.FileName.ShouldBe("Formular.pdf");
    }

    [Fact]
    public async Task Office_documents_are_downloaded_not_opened()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);
        var url = (await AttachOkAsync(api, faq, Docx(), "Anleitung.docx")).GetProperty("url").GetString()!;

        using var response = await api.SendAsync(HttpMethod.Get, url, Erika);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
    }

    [Fact]
    public async Task A_file_name_with_umlauts_survives_in_the_download_header()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);
        var url = (await AttachOkAsync(api, faq, Pdf, "Übersicht Größen.pdf")).GetProperty("url").GetString()!;

        using var response = await api.SendAsync(HttpMethod.Get, url, Erika);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("Übersicht Größen.pdf");
    }

    [Fact]
    public async Task Without_the_sign_in_a_file_cannot_be_fetched()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);
        var url = (await AttachOkAsync(api, faq, Pdf)).GetProperty("url").GetString()!;

        (await api.SendAsync(HttpMethod.Get, url, person: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_file_of_an_archived_entry_can_only_be_fetched_with_the_privilege_to_read_the_archive()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);
        var url = (await AttachOkAsync(api, faq, Pdf)).GetProperty("url").GetString()!;
        using (var archive = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{faq}", Hanna, new { archived = true }))
        {
            archive.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await api.SendAsync(HttpMethod.Get, url, Erika)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Get, url, Hanna)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_file_is_only_found_under_its_own_entry()
    {
        await using var api = await StartAsync(postgres);
        var one = await CreateEntryAsync(api, "Eins");
        var other = await CreateEntryAsync(api, "Zwei");
        var attachmentId = (await AttachOkAsync(api, one, Pdf)).GetProperty("id").GetGuid();

        (await api.SendAsync(HttpMethod.Get, $"/api/faqs/{other}/attachments/{attachmentId}", Erika)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Get, $"/api/faqs/{one}/attachments/{Guid.NewGuid()}", Erika)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Delete, $"/api/faqs/{other}/attachments/{attachmentId}", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------ what is refused

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>", "seite.html")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script/></svg>", "bild.svg")]
    [InlineData("MZ\u0090\u0000\u0003 program", "setup.exe")]
    [InlineData("#!/bin/sh\nrm -rf /", "skript.sh")]
    [InlineData("just text", "notiz.txt")]
    public async Task Anything_that_can_run_or_carry_a_script_is_refused_whatever_it_is_called(string content, string fileName)
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);

        using var response = await AttachAsync(api, faq, Hanna, Encoding.Latin1.GetBytes(content), fileName);
        using var disguised = await AttachAsync(api, faq, Hanna, Encoding.Latin1.GetBytes(content), "harmlos.pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        disguised.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        StoredFiles(api).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_file_and_one_that_is_too_big_are_refused()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);
        var tooBig = new byte[(10 * 1024 * 1024) + 1];
        Pdf.CopyTo(tooBig, 0);

        (await AttachAsync(api, faq, Hanna, [])).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AttachAsync(api, faq, Hanna, tooBig)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        StoredFiles(api).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_request_without_a_file_is_refused()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);

        using var response = await api.SendAsync(HttpMethod.Post, $"/api/faqs/{faq}/attachments", Hanna, new { nothing = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task At_most_twenty_files_can_be_attached_to_an_entry()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);
        for (var i = 0; i < 20; i++)
        {
            await AttachOkAsync(api, faq, Pdf, $"datei{i}.pdf");
        }

        using var response = await AttachAsync(api, faq, Hanna, Pdf, "eine-zu-viel.pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        StoredFiles(api).Length.ShouldBe(20);
    }

    // ------------------------------------------------------------------ removing

    [Fact]
    public async Task Removing_a_file_deletes_it_from_the_list_and_from_the_storage()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);
        var keep = await AttachOkAsync(api, faq, Pdf, "bleibt.pdf");
        var go = await AttachOkAsync(api, faq, Png, "geht.png");

        using var response = await api.SendAsync(HttpMethod.Delete, go.GetProperty("url").GetString()!, Hanna);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await EntryAsync(api, faq, Erika)).GetProperty("attachments").EnumerateArray().Select(a => a.GetProperty("fileName").GetString()).ShouldBe(["bleibt.pdf"]);
        (await api.SendAsync(HttpMethod.Get, go.GetProperty("url").GetString()!, Erika)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Get, keep.GetProperty("url").GetString()!, Erika)).StatusCode.ShouldBe(HttpStatusCode.OK);
        StoredFiles(api).Length.ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_the_entry_deletes_its_files_too()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);
        var other = await CreateEntryAsync(api, "Anderer");
        await AttachOkAsync(api, faq, Pdf);
        await AttachOkAsync(api, faq, Png, "b.png");
        await AttachOkAsync(api, other, Pdf, "bleibt.pdf");

        using var response = await api.SendAsync(HttpMethod.Delete, $"/api/faqs/{faq}", Hanna);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        StoredFiles(api).Length.ShouldBe(1); // only the file of the other entry is left
        (await EntryAsync(api, other, Erika)).GetProperty("attachments").GetArrayLength().ShouldBe(1);
    }

    // ------------------------------------------------------------------ the existing contract stays

    [Fact]
    public async Task Entries_without_files_have_an_empty_list_and_keep_all_other_fields()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);

        var entry = await EntryAsync(api, faq, Erika);

        entry.GetProperty("attachments").GetArrayLength().ShouldBe(0);
        entry.GetProperty("title").GetString().ShouldBe("Eintrag");
        entry.GetProperty("order").GetInt32().ShouldBe(0);
        entry.GetProperty("archived").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_archive_and_the_changed_entry_show_the_files_too()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateEntryAsync(api);
        await AttachOkAsync(api, faq, Pdf);

        using var changed = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{faq}", Hanna, new { title = "Neu" });
        using var archived = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{faq}", Hanna, new { archived = true });
        using var list = await api.SendAsync(HttpMethod.Get, "/api/faqs/archived", Hanna);

        (await DataAsync(changed)).GetProperty("attachments").GetArrayLength().ShouldBe(1);
        (await DataAsync(archived)).GetProperty("attachments").GetArrayLength().ShouldBe(1);
        (await DataAsync(list)).EnumerateArray().Single().GetProperty("attachments").GetArrayLength().ShouldBe(1);
    }
}
