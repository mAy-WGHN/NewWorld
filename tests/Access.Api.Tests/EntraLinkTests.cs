using System.Net;
using Access.Api;
using Access.Contracts;
using Access.Core.Acl;
using Access.Infrastructure;
using Access.Infrastructure.StepUp;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;
using static Access.Api.Tests.AccessApi;

namespace Access.Api.Tests;

/// <summary>HR's maintenance of the Microsoft account of a person: rights, reach over the person, validation, the effect on the binding.</summary>
public sealed class EntraLinkTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static Func<HttpRequestMessage, HttpRequestMessage> As(Guid person) => r => r.AsUser(person);

    private static string Url(Guid person) => $"/api/people/{person}/entra-link";

    private static SetEntraLinkRequest Body(string? upn) => new(upn);

    [Fact]
    public async Task An_administrator_links_reads_changes_and_removes_the_account()
    {
        await using var api = await StartAsync(postgres);

        using var missing = await api.SendAsync(HttpMethod.Get, Url(Erika), As(Admin));
        using var put = await api.SendAsync(HttpMethod.Put, Url(Erika), As(Admin), Body("  erika@contoso.test "));
        var linked = await api.GetAsync<EntraLinkDto>(Url(Erika), As(Admin));
        using var change = await api.SendAsync(HttpMethod.Put, Url(Erika), As(Admin), Body("erika.neu@contoso.test"));
        var changed = await api.GetAsync<EntraLinkDto>(Url(Erika), As(Admin));
        using var delete = await api.SendAsync(HttpMethod.Delete, Url(Erika), As(Admin));
        using var gone = await api.SendAsync(HttpMethod.Get, Url(Erika), As(Admin));

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).ShouldContain("NoEntraLink");
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        linked.ShouldBe(new EntraLinkDto("erika@contoso.test", false, null));
        change.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        changed.UserPrincipalName.ShouldBe("erika.neu@contoso.test");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var deleteAgain = await api.SendAsync(HttpMethod.Delete, Url(Erika), As(Admin));
        deleteAgain.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Changing_the_user_name_keeps_the_bound_account_and_removing_the_link_drops_it_with_every_confirmation()
    {
        await using var api = await StartAsync(postgres);
        var oid = Guid.NewGuid();
        (await api.SendAsync(HttpMethod.Put, Url(Erika), As(Admin), Body("erika@contoso.test"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
            var link = await db.EntraLinks.SingleAsync();
            link.EntraObjectId = oid;
            link.BoundAt = DateTimeOffset.UnixEpoch;
            db.StepUpConfirmations.Add(new StepUpConfirmationRecord { EmployeeId = Erika, ConfirmedAt = DateTimeOffset.UnixEpoch, ValidUntil = DateTimeOffset.UnixEpoch.AddYears(100) });
            db.StepUpRequests.Add(new StepUpRequestRecord { StateHash = "h", EmployeeId = Erika, Nonce = "n", CodeVerifier = "v", ExpiresAt = DateTimeOffset.UnixEpoch.AddYears(100) });
            await db.SaveChangesAsync();
        }

        (await api.SendAsync(HttpMethod.Put, Url(Erika), As(Admin), Body("erika.new@contoso.test"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterChange = await api.GetAsync<EntraLinkDto>(Url(Erika), As(Admin));
        (await api.SendAsync(HttpMethod.Delete, Url(Erika), As(Admin))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        afterChange.Bound.ShouldBeTrue();
        afterChange.BoundAt.ShouldBe(DateTimeOffset.UnixEpoch);
        await using var check = api.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AccessDbContext>();
        (await checkDb.EntraLinks.CountAsync()).ShouldBe(0);
        (await checkDb.StepUpConfirmations.CountAsync()).ShouldBe(0);
        (await checkDb.StepUpRequests.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@contoso.test")]
    [InlineData("erika@")]
    [InlineData("a@b@c.test")]
    [InlineData("erika mustermann@contoso.test")]
    public async Task An_implausible_user_name_is_422(string? upn)
    {
        await using var api = await StartAsync(postgres);

        using var response = await api.SendAsync(HttpMethod.Put, Url(Erika), As(Admin), Body(upn));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Unknown_person_is_404_and_one_account_belongs_to_one_person_only()
    {
        await using var api = await StartAsync(postgres);
        (await api.SendAsync(HttpMethod.Put, Url(Erika), As(Admin), Body("shared@contoso.test"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var unknown = await api.SendAsync(HttpMethod.Put, Url(Guid.NewGuid()), As(Admin), Body("x@contoso.test"));
        using var taken = await api.SendAsync(HttpMethod.Put, Url(Max), As(Admin), Body("SHARED@contoso.test"));
        using var same = await api.SendAsync(HttpMethod.Put, Url(Erika), As(Admin), Body("Shared@contoso.test")); // own entry may be rewritten

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        same.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Persons_without_the_privileges_and_anonymous_callers_are_refused()
    {
        await using var api = await StartAsync(postgres);
        (await api.SendAsync(HttpMethod.Put, Url(Max), As(Admin), Body("max@contoso.test"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        foreach (var (method, body) in new (HttpMethod, object?)[] { (HttpMethod.Get, null), (HttpMethod.Put, Body("x@contoso.test")), (HttpMethod.Delete, null) })
        {
            (await api.SendAsync(method, Url(Max), As(Erika), body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method}");
            (await api.SendAsync(method, Url(Max), null, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method}");
            (await api.SendAsync(method, Url(Max), r => r.AsService(AccessRoles.PermissionsRead), body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method}");
        }
    }

    [Fact]
    public async Task The_reach_over_the_person_decides_and_reading_needs_person_read_while_changing_needs_the_manage_privilege()
    {
        await using var api = await StartAsync(postgres);
        // Max manages the accounts of people in his own store (F017: Erika and himself), but not of Tom (F018). Mia has only the reading privilege.
        var manager = await api.CreateAsync("/api/roles", new CreateRoleRequest("Konten", [AccessPrivileges.EntraLinkManage, AccessPrivileges.PersonRead]), As(Admin));
        var reader = await api.CreateAsync("/api/roles", new CreateRoleRequest("Leser", [AccessPrivileges.PersonRead]), As(Admin));
        await api.CreateAsync($"/api/people/{Max}/assignments", new CreateAssignmentRequest(manager, false, [], true, null, null), As(Admin));
        await api.CreateAsync($"/api/people/{Tom}/assignments", new CreateAssignmentRequest(reader, false, [], true, null, null), As(Admin));
        (await api.SendAsync(HttpMethod.Put, Url(Tom), As(Admin), Body("tom@contoso.test"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await api.SendAsync(HttpMethod.Put, Url(Erika), As(Max), Body("erika@contoso.test"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.SendAsync(HttpMethod.Get, Url(Erika), As(Max))).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var outsidePut = await api.SendAsync(HttpMethod.Put, Url(Tom), As(Max), Body("other@contoso.test"));
        using var outsideDelete = await api.SendAsync(HttpMethod.Delete, Url(Tom), As(Max));
        using var outsideGet = await api.SendAsync(HttpMethod.Get, Url(Tom), As(Max));
        using var readerGet = await api.SendAsync(HttpMethod.Get, Url(Tom), As(Tom));
        using var readerPut = await api.SendAsync(HttpMethod.Put, Url(Tom), As(Tom), Body("tom.new@contoso.test"));

        outsidePut.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsidePut.Content.ReadAsStringAsync()).ShouldContain(nameof(GuardReason.NotInReach));
        outsideDelete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        outsideGet.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        readerGet.StatusCode.ShouldBe(HttpStatusCode.OK);
        readerPut.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.GetAsync<EntraLinkDto>(Url(Tom), As(Admin))).UserPrincipalName.ShouldBe("tom@contoso.test"); // untouched
    }

    [Fact]
    public async Task The_privilege_is_in_the_catalogue_with_a_description()
    {
        await using var api = await StartAsync(postgres);

        var entry = (await api.GetAsync<List<CatalogEntryDto>>("/api/catalog", As(Admin))).Single(c => c.Key == AccessPrivileges.EntraLinkManage);

        entry.Owner.ShouldBe("access");
        entry.Scopable.ShouldBeTrue();
        entry.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Erasure_deletes_the_link_and_every_confirmation_of_the_person()
    {
        await using var api = await StartAsync(postgres);
        (await api.SendAsync(HttpMethod.Put, Url(Erika), As(Admin), Body("erika@contoso.test"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.SendAsync(HttpMethod.Put, Url(Max), As(Admin), Body("max@contoso.test"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
            db.StepUpConfirmations.Add(new StepUpConfirmationRecord { EmployeeId = Erika, ConfirmedAt = DateTimeOffset.UnixEpoch, ValidUntil = DateTimeOffset.UnixEpoch.AddYears(100) });
            db.StepUpRequests.Add(new StepUpRequestRecord { StateHash = "h", EmployeeId = Erika, Nonce = "n", CodeVerifier = "v", ExpiresAt = DateTimeOffset.UnixEpoch.AddYears(100) });
            await db.SaveChangesAsync();
        }

        var message = new EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, Erika);
        (await api.Services.GetRequiredService<MessageDispatcher>().DispatchAsync(
            new InboundMessage(message.MessageId.ToString(), nameof(EmployeeDataErasureRequested), MessageJson.Serialize(message), 1), CancellationToken.None))
            .Outcome.ShouldBe(DispatchOutcome.Completed);

        await using var check = api.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AccessDbContext>();
        (await checkDb.EntraLinks.Select(l => l.EmployeeId).ToListAsync()).ShouldBe([Max]);
        (await checkDb.StepUpConfirmations.CountAsync()).ShouldBe(0);
        (await checkDb.StepUpRequests.CountAsync()).ShouldBe(0);
    }
}
