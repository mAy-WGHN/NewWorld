using System.Text.Json;
using System.Net;
using Rollen_Rechte.Tests.Infrastructure;

namespace Rollen_Rechte.Tests.Functional;

public class LocationTests(ApiFactory factory) : ApiTestBase(factory)
{
    // --- Lesen (öffentlich) -----------------------------------------------------------------------------------

    [Fact]
    public async Task List_IsPublic_AndHasNoDefaultLimit()
    {
        var token = Unique("Liste");
        for (var i = 0; i < 3; i++) await CreateLocationAsync(name: $"{token} {i}");

        var response = await GetAsync($"/api/locations?search={token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal(3, data.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("meta").GetProperty("limit").ValueKind);
        Assert.Equal(3, data.GetProperty("meta").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Detail_IsPublic_AndAlsoReturnsArchivedLocations()
    {
        var location = await CreateLocationAsync(archived: true);

        var response = await GetAsync($"/api/locations/{location.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal(location.StoreCode, data.GetProperty("storeCode").GetString());
        Assert.True(data.GetProperty("archived").GetBoolean());
    }

    [Fact]
    public async Task Detail_OfUnknownId_Returns404WithFriendlyMessage()
    {
        var response = await GetAsync($"/api/locations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("nicht gefunden", (await JsonAsync(response)).GetProperty("message").GetString());
    }

    // --- Anlegen ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_RequiresAdmin()
    {
        var response = await PostAsync("/api/locations", new { name = "X", storeCode = Digits(6) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsAdmin_Returns201WithLocation()
    {
        var (_, token) = await AdminWithTokenAsync();
        var code = Digits(6);

        var response = await PostAsync("/api/locations", new { name = "Neue Filiale", storeCode = code }, token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal("Neue Filiale", data.GetProperty("name").GetString());
        Assert.Equal(code, data.GetProperty("storeCode").GetString());
        Assert.False(data.GetProperty("archived").GetBoolean());
    }

    [Fact]
    public async Task Create_WithInvalidData_Returns422WithFieldErrors()
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await PostAsync("/api/locations",
            new { name = new string('x', 181), storeCode = "" }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var fields = (await JsonAsync(response)).GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Contains("name", fields);
        Assert.Contains("storeCode", fields);
    }

    [Fact]
    public async Task Create_WithDuplicateStoreCode_Returns409()
    {
        var (_, token) = await AdminWithTokenAsync();
        var existing = await CreateLocationAsync();

        var response = await PostAsync("/api/locations", new { name = "Doppelt", storeCode = existing.StoreCode }, token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Store-Code wird bereits verwendet.", (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Create_IsRateLimitedAfterFivePerMinute()
    {
        var (_, token) = await AdminWithTokenAsync();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
            statuses.Add((await PostAsync("/api/locations", new { name = $"L{i}", storeCode = Digits(8) }, token)).StatusCode);

        Assert.All(statuses.Take(5), s => Assert.Equal(HttpStatusCode.Created, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[5]);
    }

    // --- Ändern / Archivieren ---------------------------------------------------------------------------------

    [Fact]
    public async Task Update_RequiresAdmin()
    {
        var location = await CreateLocationAsync();

        var response = await PatchAsync($"/api/locations/{location.Id}", new { name = "Neu" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesNameAndStoreCode()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var newCode = Digits(7);

        var response = await PatchAsync($"/api/locations/{location.Id}", new { name = "Umbenannt", storeCode = newCode }, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal("Umbenannt", data.GetProperty("name").GetString());
        Assert.Equal(newCode, data.GetProperty("storeCode").GetString());
    }

    [Fact]
    public async Task Update_WithStoreCodeOfAnotherLocation_Returns409_ButKeepingOwnCodeIsFine()
    {
        var (_, token) = await AdminWithTokenAsync();
        var first = await CreateLocationAsync();
        var second = await CreateLocationAsync();

        Assert.Equal(HttpStatusCode.Conflict,
            (await PatchAsync($"/api/locations/{second.Id}", new { storeCode = first.StoreCode }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await PatchAsync($"/api/locations/{second.Id}", new { storeCode = second.StoreCode }, token)).StatusCode);
    }

    [Fact]
    public async Task Delete_ArchivesInsteadOfDeleting_AndMovesLocationToArchiveView()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync(name: Unique("Archiv"));

        var delete = await DeleteAsync($"/api/locations/{location.Id}", token);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var active = await DataAsync(await GetAsync($"/api/locations?search={location.Name}"));
        Assert.Equal(0, active.GetProperty("items").GetArrayLength());
        var archived = await DataAsync(await GetAsync($"/api/locations/archived?search={location.Name}", token));
        Assert.Equal(1, archived.GetProperty("items").GetArrayLength());
        Assert.True(archived.GetProperty("items")[0].GetProperty("archived").GetBoolean());
    }

    [Fact]
    public async Task Delete_RequiresAdmin()
    {
        var location = await CreateLocationAsync();

        var response = await DeleteAsync($"/api/locations/{location.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ArchiveView_RequiresAdmin()
    {
        var response = await GetAsync("/api/locations/archived");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_CanUnarchiveALocation()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync(name: Unique("Zurueck"), archived: true);

        await PatchAsync($"/api/locations/{location.Id}", new { archived = false }, token);

        var active = await DataAsync(await GetAsync($"/api/locations?search={location.Name}"));
        Assert.Equal(1, active.GetProperty("items").GetArrayLength());
    }

    // --- Suche -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Search_ByName_IsCaseInsensitive()
    {
        var token = Unique("Hamburg").ToLowerInvariant();
        await CreateLocationAsync(name: token.ToUpperInvariant());

        var data = await DataAsync(await GetAsync($"/api/locations?search={token}"));

        Assert.Equal(1, data.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Search_ByStoreCode_FindsLocation()
    {
        var code = Digits(7);
        await CreateLocationAsync(storeCode: code);

        var data = await DataAsync(await GetAsync($"/api/locations?search={code}"));

        Assert.Equal(code, data.GetProperty("items")[0].GetProperty("storeCode").GetString());
    }

    [Fact]
    public async Task Search_IgnoresLeadingZerosOfTheStoredCode()
    {
        var digits = Digits(6);
        await CreateLocationAsync(storeCode: $"000{digits}");

        // "10" findet "0010": Der Suchbegriff ohne Nullen trifft den Code mit Padding.
        var data = await DataAsync(await GetAsync($"/api/locations?search={digits}"));

        Assert.Equal(1, data.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Search_IgnoresLeadingZerosOfTheSearchTerm()
    {
        var digits = Digits(6);
        await CreateLocationAsync(storeCode: digits);

        // "0010" findet "10".
        var data = await DataAsync(await GetAsync($"/api/locations?search=000{digits}"));

        Assert.Equal(1, data.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Search_WithOnlyZeros_DoesNotMatchEverything()
    {
        var unrelated = await CreateLocationAsync(name: Unique("Ohne"), storeCode: Digits(6));

        var data = await DataAsync(await GetAsync("/api/locations?search=000"));

        Assert.DoesNotContain(data.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == unrelated.Id);
    }

    [Fact]
    public async Task Search_CombinesWithPagination_TotalCountsAllMatches()
    {
        var token = Unique("Paging");
        for (var i = 0; i < 3; i++) await CreateLocationAsync(name: $"{token} {i}");

        var data = await DataAsync(await GetAsync($"/api/locations?search={token}&limit=2&offset=0"));

        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
        Assert.Equal(3, data.GetProperty("meta").GetProperty("total").GetInt32());
        Assert.Equal(2, data.GetProperty("meta").GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task List_WithInvalidSortField_Returns422()
    {
        var response = await GetAsync("/api/locations?sortBy=password");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
