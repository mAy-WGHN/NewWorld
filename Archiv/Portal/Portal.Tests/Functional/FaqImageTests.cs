using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Portal.Tests.Functional;

public class FaqImageTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0, 1, 2, 3];
    private static readonly byte[] Webp = [.. "RIFF"u8, 10, 0, 0, 0, .. "WEBP"u8, 1, 2, 3];

    private async Task<HttpResponseMessage> UploadAsync(byte[]? content, string fileName = "bild.png", string? token = null)
    {
        var form = new MultipartFormDataContent();
        if (content is not null)
        {
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", fileName);
        }
        else
        {
            form.Add(new StringContent("irrelevant"), "other"); // gültiges Multipart, aber ohne "file"-Teil
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/faq/images") { Content = form };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }

    private async Task<(Guid Id, string Url)> UploadOkAsync(string token, byte[]? content = null, string fileName = "bild.png")
    {
        var response = await UploadAsync(content ?? Png, fileName, token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await DataAsync(response);
        return (data.GetProperty("token").GetGuid(), data.GetProperty("url").GetString()!);
    }

    private static string ImageMarkdown(Guid id) => $"![Bild](https://irs-api.example/api/faqs/images/{id})";

    private string FilePath(Guid id, string extension) => Path.Combine(Factory.UploadDir, $"{id}.{extension}");

    // --- Upload ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_RequiresAdmin()
    {
        var response = await UploadAsync(Png);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_AndPublicView_RoundTripTheBytes()
    {
        var (_, token) = await AdminWithTokenAsync();

        var (id, url) = await UploadOkAsync(token);

        Assert.StartsWith("http", url);
        Assert.EndsWith($"/api/faqs/images/{id}", url);
        var view = await GetAsync($"/api/faqs/images/{id}"); // ohne Token: öffentlich
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.Equal("image/png", view.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await view.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_DetectsTheTypeFromTheContent_NotFromTheFileName()
    {
        var (_, token) = await AdminWithTokenAsync();

        var (jpegId, _) = await UploadOkAsync(token, Jpeg, "sieht-aus-wie.png");
        var (webpId, _) = await UploadOkAsync(token, Webp, "x.jpg");

        Assert.True(File.Exists(FilePath(jpegId, "jpg")));
        Assert.True(File.Exists(FilePath(webpId, "webp")));
        Assert.Equal("image/jpeg", (await GetAsync($"/api/faqs/images/{jpegId}")).Content.Headers.ContentType!.MediaType);
        Assert.Equal("image/webp", (await GetAsync($"/api/faqs/images/{webpId}")).Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Upload_StoresTheFileOutsideTheWebRoot_UnderAnUnguessableUuidV4()
    {
        var (_, token) = await AdminWithTokenAsync();

        var (id, _) = await UploadOkAsync(token);

        Assert.Equal(4, id.Version);
        Assert.StartsWith(Path.GetFullPath(Factory.UploadDir), Path.GetFullPath(FilePath(id, "png")));
    }

    [Fact]
    public async Task Upload_OfAnUnsupportedFileType_Returns422()
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await UploadAsync("das ist kein Bild"u8.ToArray(), "text.png", token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Nur PNG-, JPEG- oder WebP-Bilder sind erlaubt.",
            (await JsonAsync(response)).GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Upload_LargerThanFiveMegabytes_Returns422()
    {
        var (_, token) = await AdminWithTokenAsync();
        var big = new byte[5 * 1024 * 1024 + 1];
        Png.CopyTo(big, 0);

        var response = await UploadAsync(big, "gross.png", token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WithoutFile_Returns400()
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await UploadAsync(null, token: token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Es wurde keine Datei übermittelt.", (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task View_OfAnUnknownToken_Returns404()
    {
        var response = await GetAsync($"/api/faqs/images/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Bild wurde nicht gefunden.", (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task View_WhenTheFileIsMissingOnDisk_Returns404()
    {
        var (_, token) = await AdminWithTokenAsync();
        var (id, _) = await UploadOkAsync(token);
        File.Delete(FilePath(id, "png"));

        var response = await GetAsync($"/api/faqs/images/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Liste / Status / Verknüpfung ---------------------------------------------------------------------------------

    [Fact]
    public async Task ListAndDelete_RequireAdmin()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/admin/faq/images")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteAsync($"/api/admin/faq/images/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task CreatingAFaq_LinksReferencedImages_AndRemovingThemMakesThemOrphaned()
    {
        var (_, token) = await AdminWithTokenAsync();
        var (imageId, _) = await UploadOkAsync(token);

        var created = await DataAsync(await PostAsync("/api/faqs",
            new { title = "Mit Bild", description = $"Siehe {ImageMarkdown(imageId)}" }, token));
        var faqId = created.GetProperty("id").GetGuid();

        var linked = await StatusOfAsync(token, imageId);
        Assert.Equal("active", linked.GetProperty("status").GetString());
        Assert.Equal(faqId, linked.GetProperty("faqId").GetGuid());
        Assert.Equal("Mit Bild", linked.GetProperty("faqTitle").GetString());

        await PatchAsync($"/api/faqs/{faqId}", new { description = "Ohne Bild" }, token);

        var orphaned = await StatusOfAsync(token, imageId);
        Assert.Equal("orphaned", orphaned.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, orphaned.GetProperty("faqId").ValueKind);
    }

    [Fact]
    public async Task ImageOfAnArchivedFaq_HasStatusArchived()
    {
        var (_, token) = await AdminWithTokenAsync();
        var (imageId, _) = await UploadOkAsync(token);
        var faq = await DataAsync(await PostAsync("/api/faqs",
            new { title = "Archiv", description = ImageMarkdown(imageId) }, token));
        await PatchAsync($"/api/faqs/{faq.GetProperty("id").GetGuid()}", new { archived = true }, token);

        var item = await StatusOfAsync(token, imageId);

        Assert.Equal("archived", item.GetProperty("status").GetString());
    }

    [Fact]
    public async Task List_FiltersByStatus_AndRejectsUnknownStatus()
    {
        var (_, token) = await AdminWithTokenAsync();
        var (orphanId, _) = await UploadOkAsync(token);
        var (activeId, _) = await UploadOkAsync(token);
        await PostAsync("/api/faqs", new { title = "Aktiv", description = ImageMarkdown(activeId) }, token);

        var orphaned = await DataAsync(await GetAsync("/api/admin/faq/images?status=orphaned", token));
        var active = await DataAsync(await GetAsync("/api/admin/faq/images?status=active", token));
        var all = await DataAsync(await GetAsync("/api/admin/faq/images", token));
        var invalid = await GetAsync("/api/admin/faq/images?status=kaputt", token);

        Assert.Contains(orphaned.EnumerateArray(), i => i.GetProperty("id").GetGuid() == orphanId);
        Assert.DoesNotContain(orphaned.EnumerateArray(), i => i.GetProperty("id").GetGuid() == activeId);
        Assert.Contains(active.EnumerateArray(), i => i.GetProperty("id").GetGuid() == activeId);
        Assert.Contains(all.EnumerateArray(), i => i.GetProperty("id").GetGuid() == orphanId);
        Assert.Contains(all.EnumerateArray(), i => i.GetProperty("id").GetGuid() == activeId);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("Ungültiger status-Filter — erlaubt sind orphaned, archived, active.",
            (await JsonAsync(invalid)).GetProperty("message").GetString());
    }

    // --- Löschen ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_OfAnOrphanedImage_RemovesRowAndFile()
    {
        var (_, token) = await AdminWithTokenAsync();
        var (id, _) = await UploadOkAsync(token);

        var response = await DeleteAsync($"/api/admin/faq/images/{id}", token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await QueryDbAsync(db => db.FaqImages.AnyAsync(i => i.Id == id)));
        Assert.False(File.Exists(FilePath(id, "png")));
    }

    [Fact]
    public async Task Delete_OfAnImageInAnArchivedFaq_IsAllowed()
    {
        var (_, token) = await AdminWithTokenAsync();
        var (id, _) = await UploadOkAsync(token);
        var faq = await DataAsync(await PostAsync("/api/faqs", new { title = "A", description = ImageMarkdown(id) }, token));
        await PatchAsync($"/api/faqs/{faq.GetProperty("id").GetGuid()}", new { archived = true }, token);

        var response = await DeleteAsync($"/api/admin/faq/images/{id}", token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OfAnImageInAnActiveFaq_Returns409_AndKeepsTheFile()
    {
        var (_, token) = await AdminWithTokenAsync();
        var (id, _) = await UploadOkAsync(token);
        await PostAsync("/api/faqs", new { title = "Aktiv", description = ImageMarkdown(id) }, token);

        var response = await DeleteAsync($"/api/admin/faq/images/{id}", token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Das Bild wird noch in einer aktiven FAQ-Antwort verwendet.",
            (await JsonAsync(response)).GetProperty("message").GetString());
        Assert.True(File.Exists(FilePath(id, "png")));
        Assert.True(await QueryDbAsync(db => db.FaqImages.AnyAsync(i => i.Id == id)));
    }

    [Fact]
    public async Task DeletingAFaq_LeavesItsImagesOrphaned()
    {
        var (_, token) = await AdminWithTokenAsync();
        var (id, _) = await UploadOkAsync(token);
        var faq = await DataAsync(await PostAsync("/api/faqs", new { title = "Weg", description = ImageMarkdown(id) }, token));

        await DeleteAsync($"/api/faqs/{faq.GetProperty("id").GetGuid()}", token);

        Assert.Equal("orphaned", (await StatusOfAsync(token, id)).GetProperty("status").GetString());
    }

    private async Task<JsonElement> StatusOfAsync(string token, Guid imageId)
    {
        var list = await DataAsync(await GetAsync("/api/admin/faq/images", token));
        return list.EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == imageId);
    }
}
