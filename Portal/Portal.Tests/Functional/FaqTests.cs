using System.Net;
using System.Text.Json;
using Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Portal.Tests.Functional;

public class FaqTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static List<Guid> Ids(JsonElement array) => array.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

    private async Task<List<Guid>> ActiveIdsAsync() => Ids(await DataAsync(await GetAsync("/api/faqs")));

    // --- Lesen -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_IsPublic_SortedByOrder_AndExcludesArchived()
    {
        var first = await CreateFaqAsync(title: Unique("Eins"));
        var second = await CreateFaqAsync(title: Unique("Zwei"));
        var archived = await CreateFaqAsync(title: Unique("Weg"), archived: true);

        var response = await GetAsync("/api/faqs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        var ids = Ids(data);
        Assert.True(ids.IndexOf(first.Id) < ids.IndexOf(second.Id));
        Assert.DoesNotContain(archived.Id, ids);
        Assert.True(data[0].TryGetProperty("order", out _)); // API-Feld heißt "order", nicht "sortOrder"
        Assert.False(data[0].TryGetProperty("sortOrder", out _));
    }

    [Fact]
    public async Task ArchivedView_RequiresAdmin_AndListsOnlyArchivedFaqs()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/faqs/archived")).StatusCode);

        var (_, token) = await AdminWithTokenAsync();
        var active = await CreateFaqAsync();
        var archived = await CreateFaqAsync(archived: true);

        var ids = Ids(await DataAsync(await GetAsync("/api/faqs/archived", token)));

        Assert.Contains(archived.Id, ids);
        Assert.DoesNotContain(active.Id, ids);
        Assert.DoesNotContain(archived.Id, await ActiveIdsAsync());
    }

    // --- Schreiben erfordert Admin -------------------------------------------------------------------------------

    [Fact]
    public async Task WriteEndpoints_RequireAdmin()
    {
        var faq = await CreateFaqAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync("/api/faqs", new { title = "t", description = "d" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PatchAsync($"/api/faqs/{faq.Id}", new { title = "t" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteAsync($"/api/faqs/{faq.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PatchAsync("/api/faqs/reorder", new { orderedIds = new[] { faq.Id } })).StatusCode);
    }

    // --- Anlegen / Ändern / Löschen ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_AppendsAtTheEnd()
    {
        var (_, token) = await AdminWithTokenAsync();
        var existing = await CreateFaqAsync(order: 100_000); // höher als alle anderen Tests (Reihenfolge der Tests ist nicht festgelegt)

        var response = await PostAsync("/api/faqs", new { title = "Neu", description = "Markdown **fett**" }, token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal(existing.SortOrder + 1, data.GetProperty("order").GetInt32());
        Assert.False(data.GetProperty("archived").GetBoolean());
    }

    [Fact]
    public async Task Create_WithInvalidData_Returns422WithFieldErrors()
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await PostAsync("/api/faqs", new { title = new string('x', 256), description = "" }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var fields = (await JsonAsync(response)).GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Contains("title", fields);
        Assert.Contains("description", fields);
    }

    [Fact]
    public async Task Update_ChangesTitleDescriptionAndArchived_ButNotTheOrder()
    {
        var (_, token) = await AdminWithTokenAsync();
        var faq = await CreateFaqAsync(order: 700);

        var data = await DataAsync(await PatchAsync($"/api/faqs/{faq.Id}",
            new { title = "Neuer Titel", description = "Neue Antwort", archived = true, order = 1 }, token));

        Assert.Equal("Neuer Titel", data.GetProperty("title").GetString());
        Assert.Equal("Neue Antwort", data.GetProperty("description").GetString());
        Assert.True(data.GetProperty("archived").GetBoolean());
        Assert.Equal(700, data.GetProperty("order").GetInt32());
    }

    [Fact]
    public async Task Delete_RemovesTheFaqPermanently()
    {
        var (_, token) = await AdminWithTokenAsync();
        var faq = await CreateFaqAsync();

        var response = await DeleteAsync($"/api/faqs/{faq.Id}", token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await QueryDbAsync(db => db.Faqs.AnyAsync(f => f.Id == faq.Id)));
    }

    [Fact]
    public async Task Update_OfUnknownFaq_Returns404()
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await PatchAsync($"/api/faqs/{Guid.NewGuid()}", new { title = "x" }, token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Reihenfolge -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Reorder_AppliesTheNewOrder_AndReturnsTheActiveList()
    {
        var (_, token) = await AdminWithTokenAsync();
        await CreateFaqAsync();
        await CreateFaqAsync();
        var current = await ActiveIdsAsync();
        var reversed = Enumerable.Reverse(current).ToList();

        var response = await PatchAsync("/api/faqs/reorder", new { orderedIds = reversed }, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal(reversed, Ids(data));
        Assert.Equal(Enumerable.Range(0, reversed.Count), data.EnumerateArray().Select(i => i.GetProperty("order").GetInt32()));
        Assert.Equal(reversed, await ActiveIdsAsync());
    }

    [Fact]
    public async Task Reorder_RejectsAnIncompleteList()
    {
        var (_, token) = await AdminWithTokenAsync();
        await CreateFaqAsync();
        await CreateFaqAsync();
        var current = await ActiveIdsAsync();

        var response = await PatchAsync("/api/faqs/reorder", new { orderedIds = current.Skip(1).ToList() }, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Die Reihenfolge muss genau die aktiven FAQ-Einträge enthalten.",
            (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Reorder_RejectsUnknownIdsAndArchivedFaqs()
    {
        var (_, token) = await AdminWithTokenAsync();
        await CreateFaqAsync();
        var archived = await CreateFaqAsync(archived: true);
        var current = await ActiveIdsAsync();

        var withUnknown = await PatchAsync("/api/faqs/reorder", new { orderedIds = current.Append(Guid.NewGuid()).ToList() }, token);
        var withArchived = await PatchAsync("/api/faqs/reorder", new { orderedIds = current.Append(archived.Id).ToList() }, token);

        Assert.Equal(HttpStatusCode.BadRequest, withUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withArchived.StatusCode);
    }

    [Fact]
    public async Task Reorder_RejectsDuplicatesAndEmptyLists_With422()
    {
        var (_, token) = await AdminWithTokenAsync();
        var faq = await CreateFaqAsync();

        var duplicates = await PatchAsync("/api/faqs/reorder", new { orderedIds = new[] { faq.Id, faq.Id } }, token);
        var empty = await PatchAsync("/api/faqs/reorder", new { orderedIds = Array.Empty<Guid>() }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicates.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
    }
}
