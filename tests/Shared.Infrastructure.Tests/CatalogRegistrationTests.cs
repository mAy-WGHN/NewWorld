using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shared.Infrastructure.Authorization;

namespace Shared.Infrastructure.Tests;

public class CatalogRegistrationTests
{
    private sealed class ScriptedHandler(params HttpStatusCode[] answers) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _answers = new(answers);

        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(_answers.Count > 0 ? _answers.Dequeue() : HttpStatusCode.NoContent);
        }
    }

    private static readonly CatalogPrivilege[] Privileges = [new("notification:read-own", false), new("absence:approve", true)];

    private static CatalogRegistrationService Service(ScriptedHandler handler, FakeTimeProvider time) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://access") }, "notifications", Privileges, time, TimeSpan.FromSeconds(30),
            NullLogger<CatalogRegistrationService>.Instance);

    [Fact]
    public async Task Sends_the_catalogue_of_the_application_to_access()
    {
        var handler = new ScriptedHandler(HttpStatusCode.NoContent);

        (await Service(handler, new FakeTimeProvider()).RegisterOnceAsync(CancellationToken.None)).ShouldBe(RegistrationResult.Registered);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Put);
        request.Path.ShouldBe("/internal/apps/notifications/catalog");
        using var body = JsonDocument.Parse(request.Body);
        body.RootElement.GetProperty("privileges").EnumerateArray().Select(p => (p.GetProperty("key").GetString(), p.GetProperty("scopable").GetBoolean()))
            .ShouldBe([("notification:read-own", false), ("absence:approve", true)]);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Temporary_problems_are_retried_later(HttpStatusCode status) =>
        (await Service(new ScriptedHandler(status), new FakeTimeProvider()).RegisterOnceAsync(CancellationToken.None)).ShouldBe(RegistrationResult.Retry);

    [Fact]
    public async Task A_service_token_that_cannot_be_created_is_retried_instead_of_crashing_the_service()
    {
        // e.g. ServiceAuth:PrivateKey not configured yet: the token handler throws InvalidOperationException.
        using var http = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://access") };
        var service = new CatalogRegistrationService(http, "notifications", Privileges, new FakeTimeProvider(), TimeSpan.FromSeconds(30),
            NullLogger<CatalogRegistrationService>.Instance);

        (await service.RegisterOnceAsync(CancellationToken.None)).ShouldBe(RegistrationResult.Retry);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("ServiceAuth:PrivateKey is not configured.");
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Rejections_are_final_because_retrying_cannot_fix_them(HttpStatusCode status) =>
        (await Service(new ScriptedHandler(status), new FakeTimeProvider()).RegisterOnceAsync(CancellationToken.None)).ShouldBe(RegistrationResult.Rejected);

    [Fact]
    public async Task Service_keeps_trying_until_access_answers_and_then_stops()
    {
        var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.NoContent);
        var time = new FakeTimeProvider();
        var service = Service(handler, time);

        await service.StartAsync(CancellationToken.None);
        await WaitFor(() => handler.Requests.Count == 1);
        time.Advance(TimeSpan.FromSeconds(30));
        await WaitFor(() => handler.Requests.Count == 2);
        time.Advance(TimeSpan.FromSeconds(30));
        await WaitFor(() => handler.Requests.Count == 3);
        time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        handler.Requests.Count.ShouldBe(3);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue();
    }
}
