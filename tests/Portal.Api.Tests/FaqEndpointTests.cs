using System.Net;
using System.Text.Json;
using Portal.Core;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// The FAQ through the real API and database, compatible with the existing frontend: <c>{success,data}</c> envelope, the field "order", 204 without body,
/// 422 with <c>errors[{field,message}]</c>. Reading the active entries and viewing images is public; administration needs privileges.
/// </summary>
public sealed class FaqEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static async Task<JsonElement> CreateAsync(PortalApi api, string title, string description = "Antwort")
    {
        using var response = await api.SendAsync(HttpMethod.Post, "/api/faqs", Hanna, new { title, description });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private static async Task<List<string>> TitlesAsync(PortalApi api, string url = "/api/faqs", Guid? person = null)
    {
        using var response = await api.SendAsync(HttpMethod.Get, url, person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await DataAsync(response)).EnumerateArray().Select(e => e.GetProperty("title").GetString()!).ToList();
    }

    private static async Task<Guid> UploadAsync(PortalApi api)
    {
        using var response = await api.UploadImageAsync(Hanna, Png);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await DataAsync(response)).GetProperty("token").GetGuid();
    }

    // ------------------------------------------------------------------ list (public)

    [Fact]
    public async Task The_active_entries_are_public_in_the_order_of_their_position_and_use_the_field_order()
    {
        await using var api = await StartAsync(postgres);
        var first = await CreateAsync(api, "Erste");
        await CreateAsync(api, "Zweite");

        using var response = await api.SendAsync(HttpMethod.Get, "/api/faqs", person: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = (await DataAsync(response)).EnumerateArray().ToList();
        items.Select(i => i.GetProperty("title").GetString()).ShouldBe(["Erste", "Zweite"]);
        items.Select(i => i.GetProperty("order").GetInt32()).ShouldBe([0, 1]);
        items[0].GetProperty("id").GetGuid().ShouldBe(first.GetProperty("id").GetGuid());
        items[0].GetProperty("archived").GetBoolean().ShouldBeFalse();
        items[0].GetProperty("createdAt").GetDateTimeOffset().ShouldBe(Start);
    }

    [Fact]
    public async Task Archived_entries_are_not_in_the_public_list_but_in_the_archive_for_those_who_may_read_it()
    {
        await using var api = await StartAsync(postgres);
        var gone = await CreateAsync(api, "Alt");
        await CreateAsync(api, "Neu");
        using var archive = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{gone.GetProperty("id").GetGuid()}", Hanna, new { archived = true });
        archive.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await TitlesAsync(api)).ShouldBe(["Neu"]);
        (await TitlesAsync(api, "/api/faqs/archived", Hanna)).ShouldBe(["Alt"]);
        using var denied = await api.SendAsync(HttpMethod.Get, "/api/faqs/archived", Erika);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var anonymous = await api.SendAsync(HttpMethod.Get, "/api/faqs/archived", person: null);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ create

    [Fact]
    public async Task A_new_entry_gets_the_position_after_all_entries_including_archived_ones()
    {
        await using var api = await StartAsync(postgres);
        var first = await CreateAsync(api, "A");
        first.GetProperty("order").GetInt32().ShouldBe(0);
        using var archive = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{first.GetProperty("id").GetGuid()}", Hanna, new { archived = true });
        archive.StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await CreateAsync(api, "  B  ");

        second.GetProperty("order").GetInt32().ShouldBe(1);
        second.GetProperty("title").GetString().ShouldBe("B"); // trimmed
    }

    [Fact]
    public async Task Creating_needs_the_privilege_and_the_error_is_in_the_envelope()
    {
        await using var api = await StartAsync(postgres);

        using var denied = await api.SendAsync(HttpMethod.Post, "/api/faqs", Erika, new { title = "x", description = "y" });
        using var anonymous = await api.SendAsync(HttpMethod.Post, "/api/faqs", person: null, new { title = "x", description = "y" });

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var body = await ErrorAsync(denied);
        body.GetProperty("message").GetString().ShouldBe("Sie haben nicht die erforderliche Berechtigung, diese Inhalte zu erstellen.");
        body.GetProperty("errors").GetArrayLength().ShouldBe(0);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await TitlesAsync(api)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "Text", "title")]
    [InlineData("   ", "Text", "title")]
    [InlineData("Titel", "", "description")]
    public async Task Missing_values_are_a_422_with_the_field(string title, string description, string field)
    {
        await using var api = await StartAsync(postgres);

        using var response = await api.SendAsync(HttpMethod.Post, "/api/faqs", Hanna, new { title, description });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var error = (await ErrorAsync(response)).GetProperty("errors")[0];
        error.GetProperty("field").GetString().ShouldBe(field);
        error.GetProperty("message").GetString().ShouldBe("Dieser Wert darf nicht leer sein.");
    }

    [Fact]
    public async Task A_title_longer_than_255_characters_is_refused()
    {
        await using var api = await StartAsync(postgres);

        using var response = await api.SendAsync(HttpMethod.Post, "/api/faqs", Hanna, new { title = new string('x', 256), description = "y" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorAsync(response)).GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe("title");
    }

    [Fact]
    public async Task A_body_that_is_not_JSON_is_a_422_in_the_envelope()
    {
        await using var api = await StartAsync(postgres);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/faqs") { Content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json") };
        request.AsUser(Hanna);

        using var response = await api.Http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorAsync(response)).GetProperty("message").GetString().ShouldNotBeNullOrEmpty();
    }

    // ------------------------------------------------------------------ update, reorder, delete

    [Fact]
    public async Task Changing_only_what_is_sent_keeps_the_rest()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateAsync(api, "Titel", "Text");
        var id = faq.GetProperty("id").GetGuid();

        using var response = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{id}", Hanna, new { title = "Neu" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await DataAsync(response);
        data.GetProperty("title").GetString().ShouldBe("Neu");
        data.GetProperty("description").GetString().ShouldBe("Text");
    }

    [Fact]
    public async Task Changing_or_deleting_an_unknown_entry_is_a_404_and_needs_the_privilege()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateAsync(api, "A");
        var id = faq.GetProperty("id").GetGuid();

        using var unknown = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{Guid.NewGuid()}", Hanna, new { title = "x" });
        using var unknownDelete = await api.SendAsync(HttpMethod.Delete, $"/api/faqs/{Guid.NewGuid()}", Hanna);
        using var deniedPatch = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{id}", Erika, new { title = "x" });
        using var deniedDelete = await api.SendAsync(HttpMethod.Delete, $"/api/faqs/{id}", Erika);

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknownDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorAsync(unknown)).GetProperty("message").GetString().ShouldStartWith("Der angeforderte Datensatz wurde nicht gefunden");
        deniedPatch.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        deniedDelete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorAsync(deniedPatch)).GetProperty("message").GetString().ShouldBe("Sie haben nicht die erforderliche Berechtigung, diese Inhalte zu bearbeiten.");
        (await ErrorAsync(deniedDelete)).GetProperty("message").GetString().ShouldBe("Sie haben nicht die erforderliche Berechtigung, diese Inhalte zu löschen.");
        (await TitlesAsync(api)).ShouldBe(["A"]);
    }

    [Fact]
    public async Task Deleting_is_final_and_answers_204_without_a_body()
    {
        await using var api = await StartAsync(postgres);
        var faq = await CreateAsync(api, "A");

        using var response = await api.SendAsync(HttpMethod.Delete, $"/api/faqs/{faq.GetProperty("id").GetGuid()}", Hanna);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        (await TitlesAsync(api)).ShouldBeEmpty();
        (await TitlesAsync(api, "/api/faqs/archived", Hanna)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_order_must_name_exactly_the_active_entries_and_then_applies()
    {
        await using var api = await StartAsync(postgres);
        var a = (await CreateAsync(api, "A")).GetProperty("id").GetGuid();
        var b = (await CreateAsync(api, "B")).GetProperty("id").GetGuid();
        var c = (await CreateAsync(api, "C")).GetProperty("id").GetGuid();

        using var reordered = await api.SendAsync(HttpMethod.Patch, "/api/faqs/reorder", Hanna, new { orderedIds = new[] { c, a, b } });

        reordered.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DataAsync(reordered)).EnumerateArray().Select(e => e.GetProperty("title").GetString()).ShouldBe(["C", "A", "B"]);
        (await TitlesAsync(api)).ShouldBe(["C", "A", "B"]);

        using var missing = await api.SendAsync(HttpMethod.Patch, "/api/faqs/reorder", Hanna, new { orderedIds = new[] { a, b } });
        using var foreign = await api.SendAsync(HttpMethod.Patch, "/api/faqs/reorder", Hanna, new { orderedIds = new[] { a, b, c, Guid.NewGuid() } });
        using var twice = await api.SendAsync(HttpMethod.Patch, "/api/faqs/reorder", Hanna, new { orderedIds = new[] { a, a, b, c } });
        using var empty = await api.SendAsync(HttpMethod.Patch, "/api/faqs/reorder", Hanna, new { orderedIds = Array.Empty<Guid>() });
        using var denied = await api.SendAsync(HttpMethod.Patch, "/api/faqs/reorder", Erika, new { orderedIds = new[] { a, b, c } });

        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        foreign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        twice.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        empty.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TitlesAsync(api)).ShouldBe(["C", "A", "B"]);
    }

    // ------------------------------------------------------------------ images

    [Fact]
    public async Task An_uploaded_image_has_an_absolute_url_and_is_public_to_view_with_the_right_content_type()
    {
        await using var api = await StartAsync(postgres);

        using var upload = await api.UploadImageAsync(Hanna, Png);

        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var data = await DataAsync(upload);
        var token = data.GetProperty("token").GetGuid();
        data.GetProperty("url").GetString().ShouldEndWith($"/api/faqs/images/{token}");
        data.GetProperty("url").GetString().ShouldStartWith("http");
        using var view = await api.SendAsync(HttpMethod.Get, $"/api/faqs/images/{token}", person: null);
        view.StatusCode.ShouldBe(HttpStatusCode.OK);
        view.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await view.Content.ReadAsByteArrayAsync()).ShouldBe(Png);
        Directory.GetFiles(api.ImageFolder).Select(Path.GetFileName).ShouldBe([$"{token}.png"]); // only under the random id
    }

    [Fact]
    public async Task Only_images_are_accepted_recognised_by_their_content_and_not_too_large()
    {
        await using var api = await StartAsync(postgres);

        using var notAnImage = await api.UploadImageAsync(Hanna, "%PDF-1.7 hello"u8.ToArray(), "bild.png");
        using var tooLarge = await api.UploadImageAsync(Hanna, [.. Png, .. new byte[5 * 1024 * 1024]]);
        using var denied = await api.UploadImageAsync(Erika, Png);
        using var anonymous = await api.UploadImageAsync(null, Png);
        using var noFile = await api.SendAsync(HttpMethod.Post, "/api/admin/faq/images", Hanna);

        notAnImage.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorAsync(notAnImage)).GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe("file");
        tooLarge.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        noFile.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Directory.Exists(api.ImageFolder).ShouldBeFalse(); // nothing was stored
    }

    [Fact]
    public async Task An_unknown_image_is_a_404()
    {
        await using var api = await StartAsync(postgres);

        using var response = await api.SendAsync(HttpMethod.Get, $"/api/faqs/images/{Guid.NewGuid()}", person: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorAsync(response)).GetProperty("message").GetString().ShouldBe("Bild wurde nicht gefunden.");
    }

    [Fact]
    public async Task The_status_of_an_image_follows_the_entries_that_link_it()
    {
        await using var api = await StartAsync(postgres);
        var image = await UploadAsync(api);

        async Task<(string Status, Guid? Faq)> StatusAsync()
        {
            using var list = await api.SendAsync(HttpMethod.Get, "/api/admin/faq/images", Hanna);
            var row = (await DataAsync(list)).EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == image);
            return (row.GetProperty("status").GetString()!, row.GetProperty("faqId").ValueKind == JsonValueKind.Null ? null : row.GetProperty("faqId").GetGuid());
        }

        (await StatusAsync()).ShouldBe(("orphaned", null));

        var faq = await CreateAsync(api, "Mit Bild", $"![x](https://host/api/faqs/images/{image})");
        var faqId = faq.GetProperty("id").GetGuid();
        (await StatusAsync()).ShouldBe(("active", faqId));

        (await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{faqId}", Hanna, new { archived = true })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusAsync()).ShouldBe(("archived", faqId));

        (await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{faqId}", Hanna, new { archived = false })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{faqId}", Hanna, new { description = "ohne Bild" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusAsync()).ShouldBe(("orphaned", null));
    }

    [Fact]
    public async Task The_list_of_images_can_be_filtered_and_an_invalid_filter_is_refused()
    {
        await using var api = await StartAsync(postgres);
        var used = await UploadAsync(api);
        var orphan = await UploadAsync(api);
        await CreateAsync(api, "F", $"/api/faqs/images/{used}");

        using var onlyOrphans = await api.SendAsync(HttpMethod.Get, "/api/admin/faq/images?status=orphaned", Hanna);
        using var invalid = await api.SendAsync(HttpMethod.Get, "/api/admin/faq/images?status=kaputt", Hanna);
        using var denied = await api.SendAsync(HttpMethod.Get, "/api/admin/faq/images", Erika);

        (await DataAsync(onlyOrphans)).EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ShouldBe([orphan]);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_image_in_an_active_answer_cannot_be_deleted_but_an_orphan_or_archived_one_can_and_the_file_goes()
    {
        await using var api = await StartAsync(postgres);
        var image = await UploadAsync(api);
        var faq = await CreateAsync(api, "F", $"![x](/api/faqs/images/{image})");

        using var conflict = await api.SendAsync(HttpMethod.Delete, $"/api/admin/faq/images/{image}", Hanna);
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorAsync(conflict)).GetProperty("message").GetString().ShouldBe("Das Bild wird noch in einer aktiven FAQ-Antwort verwendet.");

        (await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{faq.GetProperty("id").GetGuid()}", Hanna, new { archived = true })).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var denied = await api.SendAsync(HttpMethod.Delete, $"/api/admin/faq/images/{image}", Erika);
        using var deleted = await api.SendAsync(HttpMethod.Delete, $"/api/admin/faq/images/{image}", Hanna);
        using var again = await api.SendAsync(HttpMethod.Delete, $"/api/admin/faq/images/{image}", Hanna);
        using var view = await api.SendAsync(HttpMethod.Get, $"/api/faqs/images/{image}", person: null);

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        view.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Directory.GetFiles(api.ImageFolder).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_an_entry_leaves_its_images_orphaned()
    {
        await using var api = await StartAsync(postgres);
        var image = await UploadAsync(api);
        var faq = await CreateAsync(api, "F", $"/api/faqs/images/{image}");

        (await api.SendAsync(HttpMethod.Delete, $"/api/faqs/{faq.GetProperty("id").GetGuid()}", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var list = await api.SendAsync(HttpMethod.Get, "/api/admin/faq/images?status=orphaned", Hanna);
        (await DataAsync(list)).EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ShouldBe([image]);
    }

    [Fact]
    public async Task The_FAQ_privileges_are_independent_of_each_other()
    {
        await using var api = await StartAsync(postgres);
        api.Grant(Ben, TestGrant.Everywhere(PortalPrivileges.FaqRead));

        using var read = await api.SendAsync(HttpMethod.Get, "/api/faqs/archived", Ben);
        using var create = await api.SendAsync(HttpMethod.Post, "/api/faqs", Ben, new { title = "x", description = "y" });

        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
