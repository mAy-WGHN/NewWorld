using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace AppHost.Tests;

/// <summary>
/// The running landscape as seen from the tests: HTTP clients per service, people with real logins (personnel number + PIN against
/// the BC fake), the WireMock BC (scenarios and request journal) and the one-time setup of roles and responsibilities.
/// </summary>
public sealed class Landscape
{
    public const string Pin = "4711";
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);
    private static readonly SemaphoreSlim SetupLock = new(1, 1);
    private static Landscape? _instance;

    private readonly AppHostFixture _fixture;
    private readonly Dictionary<string, string> _tokens = [];

    private Landscape(AppHostFixture fixture) => _fixture = fixture;

    public Guid StoreId { get; private set; }

    public Dictionary<string, Guid> Employees { get; } = [];

    public static async Task<Landscape> GetAsync(AppHostFixture fixture)
    {
        await SetupLock.WaitAsync();
        try
        {
            if (_instance is null)
            {
                var landscape = new Landscape(fixture);
                await landscape.SetUpAsync();
                _instance = landscape;
            }

            return _instance;
        }
        finally
        {
            SetupLock.Release();
        }
    }

    // ---- polling ----------------------------------------------------------------------------------------------------------------

    public static async Task<T> UntilAsync<T>(string what, Func<Task<T?>> probe, TimeSpan? timeout = null)
        where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(90));
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await probe() is { } result)
                {
                    return result;
                }
            }
            catch (Exception e) when (e is HttpRequestException or NpgsqlException or JsonException or InvalidOperationException)
            {
                last = e;
            }

            await Task.Delay(Step);
        }

        throw new TimeoutException($"Timed out waiting for: {what}" + (last is null ? "" : $" (last error: {last.GetType().Name})"));
    }

    // ---- HTTP -------------------------------------------------------------------------------------------------------------------

    public HttpClient Client(string service) => _fixture.App.CreateHttpClient(service);

    public async Task<HttpResponseMessage> SendAsync(string service, HttpMethod method, string url, string? personnelNumber = null, object? body = null)
    {
        using var client = Client(service);
        using var request = new HttpRequestMessage(method, url);
        if (personnelNumber is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(personnelNumber));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    public async Task<T> GetAsync<T>(string service, string url, string personnelNumber)
    {
        using var response = await SendAsync(service, HttpMethod.Get, url, personnelNumber);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{service} {url}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public async Task<JsonElement> PostJsonAsync(string service, string url, string personnelNumber, object? body = null)
    {
        using var response = await SendAsync(service, HttpMethod.Post, url, personnelNumber, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{service} {url}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>())!;
    }

    private async Task<string> TokenAsync(string personnelNumber)
    {
        if (_tokens.TryGetValue(personnelNumber, out var cached))
        {
            return cached;
        }

        // The employee copy in Access arrives through the bus; until then the login says "unknown".
        var token = await UntilAsync($"login of {personnelNumber}", async () =>
        {
            using var client = Client("access-api");
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { personnelNumber, pin = Pin });
            return response.StatusCode == HttpStatusCode.OK
                ? (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()
                : null;
        });
        _tokens[personnelNumber] = token;
        return token;
    }

    /// <summary>Internal id of the person (assigned by BC Integration, identical in the token's <c>employee_id</c> claim).</summary>
    public Guid EmployeeIdOf(string personnelNumber) => Employees[personnelNumber];

    // ---- database (read only, for assertions about the landscape) -------------------------------------------------------------

    public async Task<List<string>> QueryAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(await _fixture.App.GetConnectionStringAsync(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetValue(0).ToString()!);
        }

        return rows;
    }

    // ---- WireMock BC ------------------------------------------------------------------------------------------------------------

    private static readonly string FixtureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "Bc"));

    /// <summary>Loads the mapping files of a scenario into the running BC fake; returns the ids to remove them again.</summary>
    public async Task<IReadOnlyList<string>> LoadScenarioAsync(string scenario)
    {
        using var mock = Client("bc-mock");
        var ids = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(FixtureRoot, scenario), "*.json"))
        {
            using var content = new StringContent(await File.ReadAllTextAsync(file), System.Text.Encoding.UTF8, "application/json");
            using var response = await mock.PostAsync("/__admin/mappings", content);
            response.EnsureSuccessStatusCode();
            ids.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Guid").GetString()!);
        }

        using var reset = await mock.PostAsync("/__admin/scenarios/reset", null);
        reset.EnsureSuccessStatusCode();
        return ids;
    }

    public async Task UnloadScenarioAsync(IEnumerable<string> ids)
    {
        using var mock = Client("bc-mock");
        foreach (var id in ids)
        {
            using var response = await mock.DeleteAsync($"/__admin/mappings/{id}");
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>Requests the BC fake received for absences (method, body, status code of the answer).</summary>
    public async Task<List<(string Method, string Body, int Status)>> AbsenceRequestsAsync()
    {
        using var mock = Client("bc-mock");
        var journal = await mock.GetFromJsonAsync<JsonElement>("/__admin/requests");
        return [.. journal.EnumerateArray()
            .Where(e => e.GetProperty("Request").GetProperty("Path").GetString()!.Contains("employeeAbsences", StringComparison.Ordinal))
            .Select(e => (
                e.GetProperty("Request").GetProperty("Method").GetString()!,
                e.GetProperty("Request").TryGetProperty("Body", out var b) ? b.GetString() ?? "" : "",
                e.GetProperty("Response").GetProperty("StatusCode").GetInt32()))];
    }

    // ---- one-time setup ---------------------------------------------------------------------------------------------------------

    private async Task SetUpAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        foreach (var service in new[] { "bc-integration-api", "access-api", "notifications-api", "workforce-api", "portal-api" })
        {
            await _fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(service, cts.Token);
        }

        // 1. BC Integration compares master data (WireMock BC): one store, six employees; the ids come from its events.
        var payloads = await UntilAsync("master data events", async () =>
        {
            var rows = await QueryAsync("bcintegration", "select payload from bc_integration.outbox_messages where message_type in ('EmployeeChanged','StoreChanged')");
            return rows.Count >= 7 ? rows : null;
        });
        foreach (var payload in payloads)
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("personnelNumber", out var number))
            {
                Employees[number.GetString()!] = root.GetProperty("employeeId").GetGuid();
            }
            else
            {
                StoreId = root.GetProperty("storeId").GetGuid();
            }
        }

        // 2. The services register their privileges with Access (local fake service token).
        await UntilAsync("privilege catalogues registered", async () =>
        {
            var catalog = await GetAsync<JsonElement>("access-api", "/api/catalog", "10099");
            var keys = catalog.EnumerateArray().Select(c => c.GetProperty("key").GetString()).ToHashSet();
            return keys.Contains("absence:read-own") && keys.Contains("notification:read-own") ? keys : null;
        });

        // 3. Roles and assignments (administrator 10099 is the bootstrap administrator).
        string[] own = ["absence:read-own", "absence:create-own", "absence:update-own", "notification:read-own", "notification:update-own", "preference:read-own", "preference:update-own"];
        var publicUser = Guid.Parse("a0000000-0000-0000-0000-000000000002");
        using (var update = await SendAsync("access-api", HttpMethod.Put, $"/api/roles/{publicUser}", "10099", new { privileges = own }))
        {
            update.StatusCode.ShouldBe(HttpStatusCode.NoContent, await update.Content.ReadAsStringAsync());
        }

        var colleagueRole = await CreateRoleAsync("Filiale-Mitarbeiter", ["absence:read-availability"]);
        var managerRole = await CreateRoleAsync("Filialleitung", ["absence:read-availability", "absence:read-reason", "absence:read-sync", "absence:create", "absence:approve"]);
        foreach (var number in new[] { "10042", "10044", "10045", "10046" })
        {
            await AssignAsync(number, colleagueRole);
        }

        await AssignAsync("10043", managerRole);
        var manager = EmployeeIdOf("10043");
        using var responsibility = await SendAsync("access-api", HttpMethod.Post, "/api/responsibilities", "10099", new
        {
            topic = "absence.approval",
            responsibleEmployeeId = manager,
            target = new { kind = "store", id = StoreId },
        });
        responsibility.StatusCode.ShouldBe(HttpStatusCode.OK, await responsibility.Content.ReadAsStringAsync());

        // 4. Wait until the copies reached Workforce: permissions of the people and the approvers of the employees.
        foreach (var number in Employees.Keys)
        {
            _ = await TokenAsync(number);
        }

        await UntilAsync("permission copies in Workforce", async () =>
        {
            using var own1 = await SendAsync("workforce-api", HttpMethod.Get, "/api/me/absences", "10042");
            using var store = await SendAsync("workforce-api", HttpMethod.Get, $"/api/stores/{StoreId}/absences", "10043");
            using var colleague = await SendAsync("workforce-api", HttpMethod.Get, $"/api/stores/{StoreId}/absences", "10044");
            return own1.IsSuccessStatusCode && store.IsSuccessStatusCode && colleague.IsSuccessStatusCode ? "ok" : null;
        });
        await UntilAsync("approver copies in Workforce", async () =>
        {
            var rows = await QueryAsync("workforce", "select count(*) from workforce.approver_copies where approver_ids <> '{}'");
            return rows[0] != "0" ? rows : null;
        });
    }

    private async Task<Guid> CreateRoleAsync(string name, string[] privileges)
    {
        using var response = await SendAsync("access-api", HttpMethod.Post, "/api/roles", "10099", new { name, privileges });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task AssignAsync(string personnelNumber, Guid role)
    {
        _ = await TokenAsync(personnelNumber);
        using var response = await SendAsync("access-api", HttpMethod.Post, $"/api/people/{EmployeeIdOf(personnelNumber)}/assignments", "10099",
            new { roleId = role, unrestricted = false, scopeIds = Array.Empty<Guid>(), includeHomeStore = true });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
