using System.Net;
using System.Text.Json;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// The rich-text answer of a FAQ entry through the real API and database: the document is checked and cleaned, the Markdown for old clients is derived
/// from it, the images of the document are linked to the entry, and old clients that only know the field <c>description</c> keep working.
/// </summary>
public sealed class FaqContentEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static object Doc(params object[] blocks) => new { type = "doc", content = blocks };

    private static object Paragraph(string text, params object[] marks) =>
        new { type = "paragraph", content = new object[] { marks.Length == 0 ? new { type = "text", text } : new { type = "text", text, marks } } };

    private static async Task<HttpResponseMessage> CreateAsync(PortalApi api, object body, Guid? person = null) =>
        await api.SendAsync(HttpMethod.Post, "/api/faqs", person ?? Hanna, body);

    private static async Task<JsonElement> CreatedAsync(PortalApi api, object body)
    {
        using var response = await CreateAsync(api, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private static async Task<JsonElement> ListedAsync(PortalApi api, Guid id)
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/api/faqs", person: null);
        return (await DataAsync(response)).EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == id);
    }

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
        return (await ErrorAsync(response)).GetProperty("errors").Clone();
    }

    private static async Task<(Guid Id, string Url)> UploadImageAsync(PortalApi api)
    {
        using var response = await api.UploadImageAsync(Hanna, Png);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var data = await DataAsync(response);
        return (data.GetProperty("token").GetGuid(), data.GetProperty("url").GetString()!);
    }

    // ------------------------------------------------------------------ writing a document

    [Fact]
    public async Task An_entry_is_created_from_a_document_and_gives_it_back_with_the_Markdown_for_old_clients()
    {
        await using var api = await StartAsync(postgres);

        var entry = await CreatedAsync(api, new
        {
            title = "Urlaub",
            content = Doc(
                new { type = "heading", attrs = new { level = 2 }, content = new object[] { new { type = "text", text = "So geht es" } } },
                Paragraph("Im Portal", new { type = "bold" })),
        });

        entry.GetProperty("description").GetString().ShouldBe("## So geht es\n\n**Im Portal**");
        var content = entry.GetProperty("content");
        content.GetProperty("type").GetString().ShouldBe("doc");
        content.GetProperty("content").GetArrayLength().ShouldBe(2);
        (await ListedAsync(api, entry.GetProperty("id").GetGuid())).GetProperty("content").GetProperty("content")[0].GetProperty("attrs").GetProperty("level").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Unknown_attributes_are_dropped_before_the_document_is_stored()
    {
        await using var api = await StartAsync(postgres);

        var entry = await CreatedAsync(api, new
        {
            title = "Frage",
            content = Doc(new { type = "paragraph", attrs = new { textAlign = "center", onclick = "alert(1)", style = "x" }, content = new object[] { new { type = "text", text = "a" } } }),
        });

        var attrs = entry.GetProperty("content").GetProperty("content")[0].GetProperty("attrs");
        attrs.EnumerateObject().Select(p => p.Name).ShouldBe(["textAlign"]);
    }

    [Fact]
    public async Task All_formats_survive_the_round_trip()
    {
        await using var api = await StartAsync(postgres);
        var marks = new object[]
        {
            new { type = "bold" }, new { type = "italic" }, new { type = "underline" }, new { type = "strike" },
            new { type = "link", attrs = new { href = "https://example.org" } },
            new { type = "textStyle", attrs = new { color = "#ff0000", fontSize = "24px" } },
            new { type = "highlight", attrs = new { color = "#ffff00" } },
        };
        var numbered = new { type = "orderedList", attrs = new { start = 3 }, content = new object[] { new { type = "listItem", content = new object[] { Paragraph("drei") } } } };
        var table = new { type = "table", content = new object[] { new { type = "tableRow", content = new object[] { new { type = "tableHeader", content = new object[] { Paragraph("Kopf") } } } } } };

        var entry = await CreatedAsync(api, new { title = "Alles", content = Doc(Paragraph("viel", marks), numbered, table, new { type = "horizontalRule" }) });
        var back = (await ListedAsync(api, entry.GetProperty("id").GetGuid())).GetProperty("content").GetProperty("content");

        back[0].GetProperty("content")[0].GetProperty("marks").GetArrayLength().ShouldBe(7);
        back[1].GetProperty("attrs").GetProperty("start").GetInt32().ShouldBe(3);
        back[2].GetProperty("type").GetString().ShouldBe("table");
        back[3].GetProperty("type").GetString().ShouldBe("horizontalRule");
    }

    [Fact]
    public async Task An_image_of_the_document_is_linked_to_the_entry_and_released_when_it_is_taken_out()
    {
        await using var api = await StartAsync(postgres);
        var (imageId, url) = await UploadImageAsync(api);
        var image = new { type = "image", attrs = new { src = url, alt = "Skizze" } };

        var entry = await CreatedAsync(api, new { title = "Mit Bild", content = Doc(Paragraph("Text"), image) });
        var id = entry.GetProperty("id").GetGuid();

        using (var images = await api.SendAsync(HttpMethod.Get, "/api/admin/faq/images", Hanna))
        {
            var row = (await DataAsync(images)).EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == imageId);
            row.GetProperty("status").GetString().ShouldBe("active");
            row.GetProperty("faqId").GetGuid().ShouldBe(id);
        }

        using (var changed = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{id}", Hanna, new { content = Doc(Paragraph("Ohne Bild")) }))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var again = await api.SendAsync(HttpMethod.Get, "/api/admin/faq/images", Hanna);
        (await DataAsync(again)).EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == imageId).GetProperty("status").GetString().ShouldBe("orphaned");
    }

    [Fact]
    public async Task The_document_of_an_entry_is_replaced_and_the_Markdown_follows()
    {
        await using var api = await StartAsync(postgres);
        var id = (await CreatedAsync(api, new { title = "Frage", content = Doc(Paragraph("alt")) })).GetProperty("id").GetGuid();

        using var response = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{id}", Hanna, new { content = Doc(Paragraph("neu", new { type = "italic" })) });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await DataAsync(response);
        updated.GetProperty("description").GetString().ShouldBe("*neu*");
        updated.GetProperty("content").GetProperty("content")[0].GetProperty("content")[0].GetProperty("text").GetString().ShouldBe("neu");
        updated.GetProperty("title").GetString().ShouldBe("Frage");
    }

    [Fact]
    public async Task Changing_only_the_title_leaves_the_document_alone()
    {
        await using var api = await StartAsync(postgres);
        var id = (await CreatedAsync(api, new { title = "Frage", content = Doc(Paragraph("bleibt", new { type = "bold" })) })).GetProperty("id").GetGuid();

        using var response = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{id}", Hanna, new { title = "Neue Frage" });

        var updated = await DataAsync(response);
        updated.GetProperty("title").GetString().ShouldBe("Neue Frage");
        updated.GetProperty("description").GetString().ShouldBe("**bleibt**");
        updated.GetProperty("content").GetProperty("content")[0].GetProperty("content")[0].GetProperty("marks").GetArrayLength().ShouldBe(1);
    }

    // ------------------------------------------------------------------ what is refused

    [Fact]
    public async Task A_document_with_an_element_that_is_not_allowed_is_refused_and_nothing_is_stored()
    {
        await using var api = await StartAsync(postgres);

        using var response = await CreateAsync(api, new { title = "Böse", content = Doc(new { type = "script", content = new object[] { new { type = "text", text = "alert(1)" } } }) });

        var errors = await ErrorsAsync(response);
        errors[0].GetProperty("field").GetString().ShouldBe("content");
        errors[0].GetProperty("message").GetString()!.ShouldContain("nicht erlaubt");
        using var list = await api.SendAsync(HttpMethod.Get, "/api/faqs", person: null);
        (await DataAsync(list)).GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("ftp://example.org")]
    public async Task A_link_with_another_scheme_is_refused(string href)
    {
        await using var api = await StartAsync(postgres);

        using var response = await CreateAsync(api, new { title = "Link", content = Doc(Paragraph("x", new { type = "link", attrs = new { href } })) });

        (await ErrorsAsync(response))[0].GetProperty("message").GetString()!.ShouldContain("http");
    }

    [Fact]
    public async Task An_image_from_somewhere_else_is_refused()
    {
        await using var api = await StartAsync(postgres);

        using var response = await CreateAsync(api, new { title = "Bild", content = Doc(new { type = "image", attrs = new { src = "https://evil.example/pixel.png" } }) });

        (await ErrorsAsync(response))[0].GetProperty("message").GetString()!.ShouldContain("Bilderliste");
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#ff0000; background:url(x)")]
    public async Task A_colour_that_is_no_hex_value_is_refused(string color)
    {
        await using var api = await StartAsync(postgres);

        using var response = await CreateAsync(api, new { title = "Farbe", content = Doc(Paragraph("x", new { type = "textStyle", attrs = new { color } })) });

        await ErrorsAsync(response);
    }

    [Fact]
    public async Task A_font_size_outside_the_list_is_refused()
    {
        await using var api = await StartAsync(postgres);

        using var response = await CreateAsync(api, new { title = "Größe", content = Doc(Paragraph("x", new { type = "textStyle", attrs = new { fontSize = "999px" } })) });

        (await ErrorsAsync(response))[0].GetProperty("message").GetString()!.ShouldContain("Schriftgröße");
    }

    [Fact]
    public async Task An_empty_answer_is_refused_whether_as_a_document_or_as_a_text()
    {
        await using var api = await StartAsync(postgres);

        using var emptyDoc = await CreateAsync(api, new { title = "Leer", content = Doc(new { type = "paragraph" }) });
        using var noAnswer = await CreateAsync(api, new { title = "Leer" });
        using var blankText = await CreateAsync(api, new { title = "Leer", description = "   " });

        (await ErrorsAsync(emptyDoc))[0].GetProperty("field").GetString().ShouldBe("content");
        (await ErrorsAsync(noAnswer))[0].GetProperty("field").GetString().ShouldBe("content");
        (await ErrorsAsync(blankText))[0].GetProperty("field").GetString().ShouldBe("content");
    }

    [Fact]
    public async Task Something_that_is_no_document_is_refused()
    {
        await using var api = await StartAsync(postgres);

        using var text = await CreateAsync(api, new { title = "Kein Dokument", content = "nur Text" });
        using var wrongRoot = await CreateAsync(api, new { title = "Kein Dokument", content = new { type = "paragraph" } });

        await ErrorsAsync(text);
        await ErrorsAsync(wrongRoot);
    }

    [Fact]
    public async Task A_refused_change_leaves_the_entry_as_it_was()
    {
        await using var api = await StartAsync(postgres);
        var id = (await CreatedAsync(api, new { title = "Frage", content = Doc(Paragraph("bleibt")) })).GetProperty("id").GetGuid();

        using var response = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{id}", Hanna, new { title = "Neu", content = Doc(new { type = "iframe" }) });

        await ErrorsAsync(response);
        var entry = await ListedAsync(api, id);
        entry.GetProperty("title").GetString().ShouldBe("Frage");
        entry.GetProperty("description").GetString().ShouldBe("bleibt");
    }

    [Fact]
    public async Task Writing_a_document_needs_the_privileges_like_before()
    {
        await using var api = await StartAsync(postgres);
        var id = (await CreatedAsync(api, new { title = "Frage", content = Doc(Paragraph("a")) })).GetProperty("id").GetGuid();

        using var create = await CreateAsync(api, new { title = "Neu", content = Doc(Paragraph("a")) }, Erika);
        using var change = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{id}", Erika, new { content = Doc(Paragraph("b")) });
        using var anonymous = await api.SendAsync(HttpMethod.Post, "/api/faqs", person: null, new { title = "Neu", content = Doc(Paragraph("a")) });

        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        change.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ old clients

    [Fact]
    public async Task An_old_client_that_only_sends_a_text_gets_it_back_and_a_document_made_of_its_paragraphs()
    {
        await using var api = await StartAsync(postgres);

        var entry = await CreatedAsync(api, new { title = "Alt", description = "Erste Zeile\nzweite\n\nNeuer Absatz" });

        entry.GetProperty("description").GetString().ShouldBe("Erste Zeile\nzweite\n\nNeuer Absatz");
        entry.GetProperty("content").GetProperty("content").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task A_text_that_looks_like_code_stays_a_text_in_the_document()
    {
        await using var api = await StartAsync(postgres);

        var entry = await CreatedAsync(api, new { title = "Alt", description = "<img src=x onerror=alert(1)> [x](javascript:alert(1))" });

        entry.GetProperty("content").GetProperty("content")[0].GetProperty("content")[0].GetProperty("type").GetString().ShouldBe("text");
    }

    [Fact]
    public async Task An_old_client_changes_the_answer_with_a_text_and_the_document_follows()
    {
        await using var api = await StartAsync(postgres);
        var id = (await CreatedAsync(api, new { title = "Frage", content = Doc(Paragraph("neu formatiert", new { type = "bold" })) })).GetProperty("id").GetGuid();

        using var response = await api.SendAsync(HttpMethod.Patch, $"/api/faqs/{id}", Hanna, new { description = "Nur noch Text" });

        var updated = await DataAsync(response);
        updated.GetProperty("description").GetString().ShouldBe("Nur noch Text");
        updated.GetProperty("content").GetProperty("content")[0].GetProperty("content")[0].GetProperty("text").GetString().ShouldBe("Nur noch Text");
    }

    [Fact]
    public async Task The_document_wins_if_a_client_sends_both()
    {
        await using var api = await StartAsync(postgres);

        var entry = await CreatedAsync(api, new { title = "Beides", description = "alter Text", content = Doc(Paragraph("Dokument")) });

        entry.GetProperty("description").GetString().ShouldBe("Dokument");
    }
}
