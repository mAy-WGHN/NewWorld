using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rollen_Rechte.Tests.Infrastructure;

[Collection("Api")]
public abstract class ApiTestBase
{
    public const string DefaultPassword = "test-password-123";

    protected ApiFactory Factory { get; }
    protected HttpClient Client { get; }

    protected ApiTestBase(ApiFactory factory)
    {
        Factory = factory;
        Factory.Bc.Reset();
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // Jeder Test bekommt eine eigene Fake-Client-IP, damit sich die Rate-Limiter (global 100/min, Login 25/min)
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

    // --- Hilfsdaten (direkt per ORM, nicht über HTTP) ---------------------------------------------------------

    /// <summary>Legt einen User mit der Rolle "Administrator" an (alle Rechte); mit <paramref name="withAdminRole"/> = false ganz ohne Rolle.</summary>
    protected async Task<User> CreateAdminAsync(string? username = null, string password = DefaultPassword,
        bool active = true, Action<User>? configure = null, bool withAdminRole = true)
    {
        return await Factory.WithDbAsync(async (db, sp) =>
        {
            var user = new User
            {
                Username = username ?? Unique("admin"),
                Password = sp.GetRequiredService<PasswordService>().Hash(password),
                Active = active
            };
            configure?.Invoke(user);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            if (withAdminRole)
                await AclSeeder.AssignAsync(db, user.Id, (await AclSeeder.EnsureAdministratorRoleAsync(db)).Id);
            return user;
        });
    }

    /// <summary>
    /// Ein Administrator mit <b>befristeter</b> Zuweisung (läuft in zwei Tagen ab): Er darf alles, zählt aber nicht als dauerhafter Administrator —
    /// nötig, um die "letzter Administrator"-Regel zu prüfen, ohne selbst einer zu sein.
    /// </summary>
    protected async Task<(User User, string Token)> TemporaryAdminWithTokenAsync()
    {
        var user = await CreateAdminAsync(withAdminRole: false);
        await Factory.WithDbAsync(async (db, _) =>
        {
            var role = await AclSeeder.EnsureAdministratorRoleAsync(db);
            db.AclUserRoles.Add(new AclUserRole { UserId = user.Id, AclRoleId = role.Id, ValidTo = DateTime.UtcNow.AddDays(2) });
            await db.SaveChangesAsync();
        });
        return (user, await LoginAsync(user.Username));
    }

    /// <summary>Legt einen User an, dessen einzige Rolle genau diese Rechte hat (ohne Rechte: eine leere Rolle), und liefert sein JWT.</summary>
    protected async Task<(User User, string Token)> UserWithPrivilegesAsync(params string[] privileges)
    {
        var user = await CreateAdminAsync(withAdminRole: false);
        await Factory.WithDbAsync(async (db, _) =>
        {
            var role = new AclRole { Name = Unique("Rolle"), Privileges = [.. privileges] };
            db.AclRoles.Add(role);
            db.AclUserRoles.Add(new AclUserRole { UserId = user.Id, AclRoleId = role.Id });
            await db.SaveChangesAsync();
        });
        return (user, await LoginAsync(user.Username));
    }

    /// <summary>Legt einen Admin an und liefert sein JWT.</summary>
    protected async Task<(User User, string Token)> AdminWithTokenAsync(Action<User>? configure = null)
    {
        var user = await CreateAdminAsync(configure: configure);
        return (user, await LoginAsync(user.Username));
    }

    protected async Task<string> LoginAsync(string username, string password = DefaultPassword)
    {
        var response = await PostAsync("/api/auth/login", new { username, password });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("token").GetString()!;
    }

    protected Task<Location> CreateLocationAsync(string? name = null, string? storeCode = null, bool archived = false) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var location = new Location
            {
                Name = name ?? Unique("Filiale"),
                StoreCode = storeCode ?? Random.Shared.Next(10_000_000, 99_999_999).ToString(),
                Archived = archived
            };
            db.Locations.Add(location);
            await db.SaveChangesAsync();
            return location;
        });

    protected Task<T> QueryDbAsync<T>(Func<Rollen_Rechte.Data.AccessDbContext, Task<T>> query) =>
        Factory.WithDbAsync((db, _) => query(db));
}
