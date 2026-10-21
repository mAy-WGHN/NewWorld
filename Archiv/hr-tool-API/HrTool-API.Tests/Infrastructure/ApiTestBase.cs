using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HrTool_API.Authentication;
using HrTool_API.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace HrTool_API.Tests.Infrastructure;

[Collection("Api")]
public abstract class ApiTestBase
{
    protected ApiFactory Factory { get; }
    protected HttpClient Client { get; }

    protected ApiTestBase(ApiFactory factory)
    {
        Factory = factory;
        Factory.Access.Reset();
        Factory.Locations.Reset();
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // Jeder Test bekommt eine eigene Fake-Client-IP, damit sich die Rate-Limiter (global 100/min) nicht zwischen Tests überlappen.
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", RandomIp());
    }

    protected static string RandomIp() =>
        $"10.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}";

    /// <summary>Wechselt die Fake-IP dieses Tests (z. B. um ein frisches Rate-Limit-Budget zu bekommen).</summary>
    protected void UseNewIp()
    {
        Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", RandomIp());
    }

    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    // --- Requests -------------------------------------------------------------------------------------------

    protected Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null,
        string? token = null, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        configure?.Invoke(request);
        return Client.SendAsync(request);
    }

    protected Task<HttpResponseMessage> GetAsync(string url, string? token = null) =>
        SendAsync(HttpMethod.Get, url, null, token);

    protected Task<HttpResponseMessage> PostAsync(string url, object? body, string? token = null) =>
        SendAsync(HttpMethod.Post, url, body ?? new { }, token);

    protected Task<HttpResponseMessage> PatchAsync(string url, object body, string? token = null) =>
        SendAsync(HttpMethod.Patch, url, body, token);

    protected Task<HttpResponseMessage> DeleteAsync(string url, string? token = null, object? body = null) =>
        SendAsync(HttpMethod.Delete, url, body, token);

    protected static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    protected static async Task<JsonElement> DataAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("data");

    // --- Anmeldung (Rollen-Rechte ist ersetzt: das Token ist echt signiert, die Rechte kommen vom Fake) ---------------

    /// <summary>Ein gültig signiertes Token, wie es Rollen-Rechte ausstellt (<c>sub</c>, <c>username</c>).</summary>
    protected static string NewToken(Guid? userId = null, DateTime? expires = null)
    {
        var id = userId ?? Guid.CreateVersion7();
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.JwtKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(claims: [new Claim("sub", id.ToString()), new Claim("username", Unique("user"))],
            expires: expires ?? DateTime.UtcNow.AddHours(1), signingCredentials: credentials);
        return new JwtSecurityTokenHandler { SetDefaultTimesOnTokenCreation = false }.WriteToken(token);
    }

    /// <summary>Ein User, dessen Rollen genau diese Rechte uneingeschränkt vergeben (ohne Rechte: keine Rolle).</summary>
    protected Task<(Guid UserId, string Token)> UserWithPrivilegesAsync(params string[] privileges) =>
        ScopedUserAsync(privileges, ScopeSet.Everything);

    /// <summary>Ein User, dessen Rechte nur im Scope (Filialen) gelten.</summary>
    protected Task<(Guid UserId, string Token)> ScopedUserAsync(string[] privileges, ScopeSet scope)
    {
        var userId = Guid.CreateVersion7();
        var token = NewToken(userId);
        Factory.Access.Set(token, privileges, scope);
        return Task.FromResult((userId, token));
    }

    protected Task<(Guid UserId, string Token)> ScopedUserAsync(string[] privileges, IEnumerable<Guid> locationIds) =>
        ScopedUserAsync(privileges, ScopeSet.Of(locationIds));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Guid> WorkplaceByLocation = new();

    /// <summary>Der Arbeitsort einer Filiale (je Filiale einer, wie im echten Stamm); wird beim ersten Aufruf angelegt.</summary>
    protected async Task<Guid> WorkplaceForAsync(Guid locationId)
    {
        if (WorkplaceByLocation.TryGetValue(locationId, out var existing)) return existing;
        var workplace = await Factory.WithDbAsync(async (db, _) =>
        {
            var w = new HrTool_API.Workforce.Workplace { Code = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), Name = "Test", LocationId = locationId };
            db.Workplaces.Add(w);
            await db.SaveChangesAsync();
            return w;
        });
        return WorkplaceByLocation.GetOrAdd(locationId, workplace.Id);
    }

    /// <summary>Ein Arbeitsort ohne Filiale (wie <c>BS</c> und <c>SABA</c> im Stamm).</summary>
    protected Task<HrTool_API.Workforce.Workplace> WorkplaceWithoutLocationAsync() =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var w = new HrTool_API.Workforce.Workplace { Code = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), Name = "Ohne Filiale" };
            db.Workplaces.Add(w);
            await db.SaveChangesAsync();
            return w;
        });

    protected async Task<string> AdminTokenAsync() => (await UserWithPrivilegesAsync([.. HrTool_API.Authentication.PrivilegeCatalog.All])).Token;

    protected async Task<T> InsertAsync<T>(T entity) where T : class =>
        await Factory.WithDbAsync(async (db, _) =>
        {
            db.Add(entity);
            await db.SaveChangesAsync();
            return entity;
        });

    protected LocationResponse CreateLocation(string? name = null, string? storeCode = null, bool archived = false) =>
        Factory.Locations.Add(name ?? Unique("Filiale"), storeCode ?? Random.Shared.Next(10_000_000, 99_999_999).ToString(), archived);
}
