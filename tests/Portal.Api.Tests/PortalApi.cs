using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Infrastructure.Messaging;
using Shared.Infrastructure.Messaging;
using Portal.Api;
using Portal.Core;
using Portal.Infrastructure;
using Shared.Authorization;
using Shared.Messaging;
using Shared.Testing;

namespace Portal.Api.Tests;

/// <summary>
/// The real Portal API on its own database and a temporary image folder, fixed clock (Friday 2026-10-23, 12:00 UTC). Seeded: store A and B,
/// <see cref="Hanna"/> (administration: all privileges everywhere), <see cref="Erika"/> and <see cref="Ben"/> (signed in, without privileges).
/// </summary>
public sealed class PortalApi : WebApplicationFactory<ApiMarker>
{
    public static readonly Guid StoreA = Guid.Parse("00000000-0000-0000-0000-00000000a017");
    public static readonly Guid StoreB = Guid.Parse("00000000-0000-0000-0000-00000000b018");
    public static readonly Guid StoreC = Guid.Parse("00000000-0000-0000-0000-00000000c019");

    /// <summary>Closed store: reports cannot be filed for it any more.</summary>
    public static readonly Guid StoreD = Guid.Parse("00000000-0000-0000-0000-00000000d020");
    public static readonly Guid Hanna = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    public static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    public static readonly Guid Ben = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    public static readonly Guid Revisor = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
    public static readonly Guid It = Guid.Parse("00000000-0000-0000-0000-0000000000f2");

    public static readonly DateTimeOffset Start = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6];

    private readonly string _connectionString;
    private readonly Dictionary<string, string?> _settings;

    private PortalApi(string connectionString, Dictionary<string, string?> settings)
    {
        _connectionString = connectionString;
        _settings = settings;
        ImageFolder = Path.Combine(Path.GetTempPath(), "portal-tests-" + Guid.NewGuid().ToString("N"));
    }

    public HttpClient Http { get; private set; } = null!;

    public string ImageFolder { get; }

    /// <summary>Where the files attached to FAQ entries are stored.</summary>
    public string AttachmentFolder => ImageFolder + "-files";

    public static async Task<PortalApi> StartAsync(PostgresFixture postgres, Dictionary<string, string?>? settings = null)
    {
        var api = new PortalApi(await postgres.CreateDatabaseAsync(), settings ?? []);
        api.Http = api.CreateClient();
        await api.SeedAsync();
        return api;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionName}", _connectionString);
        builder.UseSetting("Portal:Faq:ImagePath", ImageFolder);
        builder.UseSetting("Portal:Faq:AttachmentPath", AttachmentFolder);
        // The tests of one class share the client address; the limits have their own tests.
        builder.UseSetting("Portal:RateLimiting:RequestsPerMinute", "100000");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddTestAuthentication();
            services.UseFakeTimeProvider(Start);
        });
    }

    private async Task SeedAsync()
    {
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Stores.AddRange(
                new StoreRecord { StoreId = StoreA, StoreNumber = "017", Name = "Store A", IsActive = true },
                new StoreRecord { StoreId = StoreB, StoreNumber = "018", Name = "Store B", IsActive = true },
                new StoreRecord { StoreId = StoreC, StoreNumber = "019", Name = "Store C", IsActive = true },
                new StoreRecord { StoreId = StoreD, StoreNumber = "020", Name = "Store D", IsActive = false });
            db.People.AddRange(
                new PersonRecord { EmployeeId = Hanna, Name = "Hanna Admin", HomeStoreId = StoreA },
                new PersonRecord { EmployeeId = Erika, Name = "Erika Mustermann", HomeStoreId = StoreA },
                new PersonRecord { EmployeeId = Ben, Name = "Ben Beispiel", HomeStoreId = StoreB },
                new PersonRecord { EmployeeId = Revisor, Name = "Rita Revisorin", HomeStoreId = StoreA },
                new PersonRecord { EmployeeId = It, Name = "Ingo IT", HomeStoreId = StoreA });
            await db.SaveChangesAsync();
        }

        var cache = Services.GetRequiredService<IPermissionCache>();
        cache.Grant(Hanna, [.. PortalPrivileges.Catalog.Select(c => TestGrant.Everywhere(c.Key))]);
        await Task.CompletedTask;
    }

    public async Task<List<OutboxMessage>> OutboxAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        return await db.Set<OutboxMessage>().AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync();
    }

    public async Task<DispatchResult> DeliverAsync<T>(T message)
        where T : IMessage =>
        await Services.GetRequiredService<MessageDispatcher>().DispatchAsync(
            new InboundMessage(message.MessageId.ToString(), typeof(T).Name, MessageJson.Serialize(message), 1), CancellationToken.None);

    /// <summary>Files a report the way the public form does; returns the <c>data</c> of the answer.</summary>
    public async Task<System.Text.Json.JsonElement> ReportAsync(object body, Guid? person = null)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/reports", person, body);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    /// <summary>Sets the privileges of a person (replacing the former ones).</summary>
    public void Grant(Guid person, params TestGrant[] grants) =>
        Services.GetRequiredService<IPermissionCache>().Grant(person, grants);

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, Guid? person, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (person is { } p)
        {
            request.AsUser(p);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Http.SendAsync(request);
    }

    public async Task<HttpResponseMessage> UploadImageAsync(Guid? person, byte[] content, string fileName = "bild.png", string url = "/api/admin/faq/images")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (person is { } p)
        {
            request.AsUser(p);
        }

        request.Content = new MultipartFormDataContent { { new ByteArrayContent(content), "file", fileName } };
        return await Http.SendAsync(request);
    }

    /// <summary>The <c>data</c> of a successful envelope; fails if the answer is not <c>{"success":true,"data":…}</c>.</summary>
    public static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        document.RootElement.GetProperty("success").GetBoolean().ShouldBeTrue(text);
        return document.RootElement.GetProperty("data").Clone();
    }

    /// <summary>The whole body of an error envelope.</summary>
    public static async Task<JsonElement> ErrorAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        document.RootElement.GetProperty("success").GetBoolean().ShouldBeFalse(text);
        return document.RootElement.Clone();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Http?.Dispose();
            if (Directory.Exists(ImageFolder))
            {
                Directory.Delete(ImageFolder, recursive: true);
            }

            if (Directory.Exists(AttachmentFolder))
            {
                Directory.Delete(AttachmentFolder, recursive: true);
            }
        }

        base.Dispose(disposing);
    }
}
