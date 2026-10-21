using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Portal.Authentication;
using Portal.Contracts;
using Portal.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Portal.Tests.Infrastructure;

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
        Factory.Events.Reset();
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // Jeder Test bekommt eine eigene Fake-Client-IP, damit sich die Rate-Limiter (global 100/min, Meldungen 10/min)
        // nicht zwischen Tests überlappen. Das Original vergibt sie je Testklasse; je Test ist noch robuster.
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

    /// <summary>Zufällige Ziffernfolge aus 1–9 (ohne Nullen, damit Padding-Tests eindeutig bleiben).</summary>
    protected static string Digits(int length) =>
        new(Enumerable.Range(0, length).Select(_ => (char)('1' + Random.Shared.Next(9))).ToArray());

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

    /// <summary>Ein User, der in Rollen-Rechte alle Rechte dieser Anwendung uneingeschränkt hat.</summary>
    protected Task<(Guid UserId, string Token)> AdminWithTokenAsync() => UserWithPrivilegesAsync([.. PrivilegeCatalog.All]);

    /// <summary>Ein User, dessen Rechte nur im Scope (Filialen/Einzelmeldungen) gelten.</summary>
    protected Task<(Guid UserId, string Token)> ScopedUserAsync(string[] privileges, ScopeSet scope)
    {
        var userId = Guid.CreateVersion7();
        var token = NewToken(userId);
        Factory.Access.Set(token, privileges, scope);
        return Task.FromResult((userId, token));
    }

    protected Task<(Guid UserId, string Token)> ScopedUserAsync(string[] privileges, IEnumerable<Guid> locationIds, IEnumerable<Guid>? reportIds = null) =>
        ScopedUserAsync(privileges, ScopeSet.Of(locationIds, reportIds ?? []));

    // --- Hilfsdaten (direkt per ORM, nicht über HTTP) ---------------------------------------------------------

    protected Task<LocationResponse> CreateLocationAsync(string? name = null, string? storeCode = null, bool archived = false) =>
        Task.FromResult(Factory.Locations.Add(name ?? Unique("Filiale"), storeCode ?? Random.Shared.Next(10_000_000, 99_999_999).ToString(), archived));

    protected Task<Faq> CreateFaqAsync(string? title = null, string description = "Antwort", int? order = null,
        bool archived = false) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var max = await db.Faqs.Select(f => (int?)f.SortOrder).MaxAsync() ?? -1;
            var faq = new Faq
            {
                Title = title ?? Unique("Frage"),
                Description = description,
                SortOrder = order ?? max + 1,
                Archived = archived
            };
            db.Faqs.Add(faq);
            await db.SaveChangesAsync();
            return faq;
        });

    protected Task<Report> CreateReportAsync(
        LocationResponse location,
        Portal.Domain.Enums.ReportCategory category = Portal.Domain.Enums.ReportCategory.Bug,
        Portal.Domain.Enums.ReportSeverity severity = Portal.Domain.Enums.ReportSeverity.Medium,
        int personnelNumber = 4711, string description = "Beschreibung", bool archived = false,
        DateTime? createdAt = null,
        Portal.Domain.Enums.ReportSource source = Portal.Domain.Enums.ReportSource.Employee,
        params LocationResponse[] extraLocations) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var ids = extraLocations.Select(l => l.Id).Append(location.Id);
            var report = new Report(personnelNumber, source, category, severity, description, ids) { Archived = archived };
            db.Reports.Add(report);
            // CreatedAt hat keinen öffentlichen Setter (wie im Original); für Tests direkt über den Change Tracker.
            if (createdAt is not null) db.Entry(report).Property(r => r.CreatedAt).CurrentValue = createdAt.Value;
            await db.SaveChangesAsync();
            return report;
        });

    protected Task<T> QueryDbAsync<T>(Func<Portal.Data.IrsDbContext, Task<T>> query) =>
        Factory.WithDbAsync((db, _) => query(db));
}
