using System.Net;
using System.Net.Http.Json;
using Access.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;
using Workforce.Api;
using Workforce.Core.Absences;
using Workforce.Core.Certificates;
using Workforce.Infrastructure;
using Workforce.Infrastructure.People;

namespace Workforce.Api.Tests;

/// <summary>
/// The real Workforce API on its own database, fixed clock (Friday 2026-10-23, 12:00 UTC). Seeded: store A with manager Max, HR Hanna,
/// employee Erika and colleague Ben; store B with manager Lena. Erika's approver is Max.
/// </summary>
public sealed class WorkforceApi : WebApplicationFactory<ApiMarker>
{
    public static readonly Guid StoreA = Guid.Parse("00000000-0000-0000-0000-00000000a017");
    public static readonly Guid StoreB = Guid.Parse("00000000-0000-0000-0000-00000000b018");
    public static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    public static readonly Guid Ben = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    public static readonly Guid Max = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid Lena = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    public static readonly Guid Hanna = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    public static readonly Guid Sven = Guid.Parse("00000000-0000-0000-0000-0000000000f1");

    public static readonly DateTimeOffset Start = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;

    private WorkforceApi(string connectionString) => _connectionString = connectionString;

    public HttpClient Http { get; private set; } = null!;

    public static async Task<WorkforceApi> StartAsync(PostgresFixture postgres)
    {
        var api = new WorkforceApi(await postgres.CreateDatabaseAsync());
        api.Http = api.CreateClient();
        await api.SeedAsync();
        return api;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionName}", _connectionString);
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
            var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
            db.Stores.AddRange(
                new StoreRecord { StoreId = StoreA, StoreNumber = "017", Name = "Store A", IsActive = true },
                new StoreRecord { StoreId = StoreB, StoreNumber = "018", Name = "Store B", IsActive = true });
            db.People.AddRange(
                new PersonRecord { EmployeeId = Erika, Name = "Erika Mustermann", HomeStoreId = StoreA },
                new PersonRecord { EmployeeId = Ben, Name = "Ben Beispiel", HomeStoreId = StoreA },
                new PersonRecord { EmployeeId = Max, Name = "Max Leiter", HomeStoreId = StoreA },
                new PersonRecord { EmployeeId = Lena, Name = "Lena Leiterin", HomeStoreId = StoreB });
            db.ApproverCopies.Add(new ApproverCopyRecord { EmployeeId = Erika, Version = 1, ApproverIds = [Max] });
            await db.SaveChangesAsync();
        }

        var cache = Services.GetRequiredService<IPermissionCache>();
        string[] own = [AbsencePrivileges.ReadOwn, AbsencePrivileges.CreateOwn, AbsencePrivileges.UpdateOwn, CertificatePrivileges.UploadOwn];
        foreach (var person in new[] { Erika, Ben, Max, Lena, Hanna, Sven })
        {
            cache.Grant(person, own.Select(k => new TestGrant(k)).ToArray());
        }

        // Colleague: sees "absent" in the own store only.
        Grant(Ben, new TestGrant(AbsencePrivileges.ReadAvailability, [StoreA], [Erika, Ben, Max]));
        // Store managers: everything about their own store's employees.
        Grant(Max, StoreManager(StoreA, Erika, Ben, Max));
        Grant(Lena, StoreManager(StoreB, Lena));
        // HR: everywhere.
        Grant(Hanna, [.. AbsencePrivileges.Catalog.Concat(CertificatePrivileges.Catalog).Concat(Workforce.Core.Employees.EmployeePrivileges.Catalog).Select(c => TestGrant.Everywhere(c.Key))]);
    }

    private static TestGrant[] StoreManager(Guid store, params Guid[] employees) =>
    [
        .. new[] { AbsencePrivileges.ReadAvailability, AbsencePrivileges.ReadReason, AbsencePrivileges.ReadSync, AbsencePrivileges.Create, AbsencePrivileges.Approve, CertificatePrivileges.UploadFor }
            .Select(k => new TestGrant(k, [store], employees)),
    ];

    /// <summary>Own privileges stay, the given ones are added.</summary>
    public void Grant(Guid person, params TestGrant[] grants)
    {
        string[] own = [AbsencePrivileges.ReadOwn, AbsencePrivileges.CreateOwn, AbsencePrivileges.UpdateOwn, CertificatePrivileges.UploadOwn];
        Services.GetRequiredService<IPermissionCache>().Grant(person, [.. own.Where(k => grants.All(g => g.Key != k)).Select(k => new TestGrant(k)), .. grants]);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, Guid? person, object? body = null, params string[] serviceRoles)
    {
        using var request = new HttpRequestMessage(method, url);
        if (person is { } p)
        {
            request.AsUser(p);
        }

        if (serviceRoles.Length > 0)
        {
            request.AsService(serviceRoles);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Http.SendAsync(request);
    }

    public async Task<T> GetAsync<T>(string url, Guid person)
    {
        using var response = await SendAsync(HttpMethod.Get, url, person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public async Task<AbsenceDto> PostAbsenceAsync(string url, Guid person, object? body = null)
    {
        using var response = await SendAsync(HttpMethod.Post, url, person, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AbsenceDto>())!;
    }

    /// <summary>Uploads a certificate as a form (file, first and last day from the certificate).</summary>
    public async Task<HttpResponseMessage> UploadCertificateAsync(string url, Guid? person, byte[] file, string from, string to, string fileName = "au.pdf")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (person is { } p)
        {
            request.AsUser(p);
        }

        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(file), "file", fileName },
            { new StringContent(from), "certifiedFrom" },
            { new StringContent(to), "certifiedTo" },
        };
        request.Content = form;
        return await Http.SendAsync(request);
    }

    public async Task<CertificateDto> UploadOkAsync(string url, Guid person, byte[] file, string from, string to)
    {
        using var response = await UploadCertificateAsync(url, person, file, from, to);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CertificateDto>())!;
    }

    public async Task<List<OutboxMessage>> OutboxAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
        return await db.Set<OutboxMessage>().AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync();
    }

    public async Task<DispatchResult> DeliverAsync<T>(T message)
        where T : IMessage =>
        await Services.GetRequiredService<MessageDispatcher>().DispatchAsync(
            new InboundMessage(message.MessageId.ToString(), typeof(T).Name, MessageJson.Serialize(message), 1), CancellationToken.None);

    public async Task SetEntitlementAsync(Guid employee, int year, decimal days)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
        db.Entitlements.Add(new Workforce.Infrastructure.Absences.EntitlementRecord { EmployeeId = employee, Year = year, Days = days });
        await db.SaveChangesAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Http?.Dispose();
        }

        base.Dispose(disposing);
    }
}
