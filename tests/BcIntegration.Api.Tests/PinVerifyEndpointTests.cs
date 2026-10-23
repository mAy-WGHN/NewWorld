using System.Net;
using System.Net.Http.Json;
using BcIntegration.Api;
using BcIntegration.Core.Errors;
using BcIntegration.Core.Login;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shared.Testing;

namespace BcIntegration.Api.Tests;

public sealed class PinVerifyEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed class FakeVerifier(Func<string, string, PinCheck> answer) : IBcPinVerifier
    {
        public List<(string Number, string Pin)> Calls { get; } = [];

        public Task<PinCheck> VerifyAsync(string personnelNumber, string pin, CancellationToken cancellationToken)
        {
            Calls.Add((personnelNumber, pin));
            return Task.FromResult(answer(personnelNumber, pin));
        }
    }

    private async Task<(BcIntegrationApiFactory Api, HttpClient Client, FakeVerifier Verifier)> StartAsync(Func<string, string, PinCheck> answer)
    {
        var verifier = new FakeVerifier(answer);
        var api = new BcIntegrationApiFactory(await postgres.CreateDatabaseAsync(), s => s.AddScoped<IBcPinVerifier>(_ => verifier));
        return (api, api.CreateClient(), verifier);
    }

    private static async Task<HttpResponseMessage> Verify(HttpClient client, Func<HttpRequestMessage, HttpRequestMessage>? identity, string number = "10042", string pin = "4711")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/employees/verify-pin") { Content = JsonContent.Create(new VerifyPinRequest(number, pin)) };
        identity?.Invoke(request);
        return await client.SendAsync(request);
    }

    private static Func<HttpRequestMessage, HttpRequestMessage> Access => r => r.AsService(BcIntegrationRoles.PinVerify);

    [Fact]
    public async Task Caller_with_the_role_gets_yes_or_no_and_the_name()
    {
        var (api, client, _) = await StartAsync((n, p) => n == "10042" && p == "4711" ? new PinCheck(true, "Erika Mustermann") : new PinCheck(false, null));
        await using var _ = api;

        using var yes = await Verify(client, Access);
        using var no = await Verify(client, Access, pin: "0000");

        (await yes.Content.ReadFromJsonAsync<VerifyPinResponse>()).ShouldBe(new VerifyPinResponse(true, "Erika Mustermann"));
        (await no.Content.ReadFromJsonAsync<VerifyPinResponse>()).ShouldBe(new VerifyPinResponse(false, null));
    }

    [Fact]
    public async Task Without_the_role_the_endpoint_is_closed()
    {
        var (api, client, verifier) = await StartAsync((_, _) => new PinCheck(true, "x"));
        await using var _ = api;
        var person = Guid.NewGuid();
        api.Services.GetRequiredService<Shared.Authorization.IPermissionCache>().Grant(person, TestGrant.Everywhere("access:assignment:create"));

        using var anonymous = await Verify(client, null);
        using var asPerson = await Verify(client, r => r.AsUser(person));
        using var otherService = await Verify(client, r => r.AsService(BcIntegrationRoles.EmployeeErasureRequest));

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        asPerson.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        otherService.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        verifier.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "4711")]
    [InlineData("10042", "")]
    [InlineData("10042", "   ")]
    public async Task Empty_values_are_refused_without_asking_bc(string number, string pin)
    {
        var (api, client, verifier) = await StartAsync((_, _) => new PinCheck(true, "x"));
        await using var _ = api;

        using var response = await Verify(client, Access, number, pin);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        verifier.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bc_outage_is_503_without_details_so_the_caller_can_fall_back()
    {
        var (api, client, _) = await StartAsync((_, _) => throw new BcTransientFailureException("Http500"));
        await using var _ = api;

        using var response = await Verify(client, Access);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("4711");
        body.ShouldNotContain("Http500");
    }
}
