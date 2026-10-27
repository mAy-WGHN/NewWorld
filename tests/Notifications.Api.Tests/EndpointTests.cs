using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Api;
using Notifications.Core;
using Shared.Authorization;
using Shared.Testing;
using static Notifications.Api.Tests.NotificationsApi;

namespace Notifications.Api.Tests;

public sealed class EndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Stranger = Guid.Parse("00000000-0000-0000-0000-0000000000f1");

    [Fact]
    public async Task List_is_newest_first_pageable_and_filterable_by_unread()
    {
        await using var api = await StartAsync(postgres);
        for (var i = 0; i < 3; i++)
        {
            await api.DeliverAsync(Command());
            await Task.Delay(5);
        }

        var all = await api.GetAsync<List<NotificationDto>>("/api/me/notifications", Max);
        using (var read = await api.SendAsync(HttpMethod.Post, $"/api/me/notifications/{all[0].Id}/read", Max))
        {
            read.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        all.Select(n => n.CreatedAt).ShouldBe(all.Select(n => n.CreatedAt).OrderDescending());
        (await api.GetAsync<List<NotificationDto>>("/api/me/notifications?take=2", Max)).Count.ShouldBe(2);
        (await api.GetAsync<List<NotificationDto>>("/api/me/notifications?unread=true", Max)).Select(n => n.Id).ShouldNotContain(all[0].Id);
        (await api.GetAsync<List<NotificationDto>>($"/api/me/notifications?before={Uri.EscapeDataString(all[0].CreatedAt.ToString("O"))}", Max)).Count.ShouldBe(2);
        (await api.GetAsync<UnreadCountDto>("/api/me/notifications/unread-count", Max)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Mark_read_and_read_all_only_touch_the_own_notifications()
    {
        await using var api = await StartAsync(postgres);
        await api.DeliverAsync(Command(recipients: [Max, Erika]));
        var maxNotification = (await api.GetAsync<List<NotificationDto>>("/api/me/notifications", Max)).Single();

        using var foreign = await api.SendAsync(HttpMethod.Post, $"/api/me/notifications/{maxNotification.Id}/read", Erika);
        using var missing = await api.SendAsync(HttpMethod.Post, $"/api/me/notifications/{Guid.NewGuid()}/read", Erika);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);   // someone else's looks like a missing one
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.GetAsync<UnreadCountDto>("/api/me/notifications/unread-count", Max)).Count.ShouldBe(1);

        using var all = await api.SendAsync(HttpMethod.Post, "/api/me/notifications/read-all", Erika);
        all.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetAsync<UnreadCountDto>("/api/me/notifications/unread-count", Erika)).Count.ShouldBe(0);
        (await api.GetAsync<UnreadCountDto>("/api/me/notifications/unread-count", Max)).Count.ShouldBe(1);
        using var twice = await api.SendAsync(HttpMethod.Post, $"/api/me/notifications/{maxNotification.Id}/read", Max);
        twice.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var again = await api.SendAsync(HttpMethod.Post, $"/api/me/notifications/{maxNotification.Id}/read", Max);
        again.StatusCode.ShouldBe(HttpStatusCode.NoContent); // idempotent
    }

    [Fact]
    public async Task Preferences_are_listed_per_type_and_channel_and_can_be_changed_for_optional_types_only()
    {
        await using var api = await StartAsync(postgres);

        var before = await api.GetAsync<List<NotificationTypeDto>>("/api/me/notification-preferences", Max);
        before.Select(t => t.TemplateKey).ShouldBe(
        ["workforce.sick-note-certificate-rejected.employee", "workforce.sick-note-certificate-reminder.employee", "workforce.sick-note.store-manager", "workforce.vacation-decision.requester", "workforce.vacation-request.supervisor"]);
        before.Single(t => t.TemplateKey == "workforce.sick-note.store-manager").Mandatory.ShouldBeTrue();
        before.SelectMany(t => t.Channels).ShouldAllBe(c => c.Enabled);

        using var off = await api.SendAsync(HttpMethod.Put, "/api/me/notification-preferences/workforce.vacation-decision.requester/Push", Max, new SetPreferenceRequest(false));
        using var mandatory = await api.SendAsync(HttpMethod.Put, "/api/me/notification-preferences/workforce.sick-note.store-manager/Push", Max, new SetPreferenceRequest(false));
        using var unknown = await api.SendAsync(HttpMethod.Put, "/api/me/notification-preferences/portal.unknown.employee/Push", Max, new SetPreferenceRequest(false));
        using var badChannel = await api.SendAsync(HttpMethod.Put, "/api/me/notification-preferences/workforce.vacation-decision.requester/Carrierpigeon", Max, new SetPreferenceRequest(false));

        off.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        mandatory.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        badChannel.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var after = await api.GetAsync<List<NotificationTypeDto>>("/api/me/notification-preferences", Max);
        after.Single(t => t.TemplateKey == "workforce.vacation-decision.requester").Channels.Single(c => c.Channel == "Push").Enabled.ShouldBeFalse();
        (await api.GetAsync<List<NotificationTypeDto>>("/api/me/notification-preferences", Erika)).SelectMany(t => t.Channels).ShouldAllBe(c => c.Enabled); // others unaffected
    }

    public static TheoryData<string, string, string> Endpoints => new()
    {
        { "GET", "/api/me/notifications", NotificationPrivileges.ReadOwn },
        { "GET", "/api/me/notifications/unread-count", NotificationPrivileges.ReadOwn },
        { "POST", "/api/me/notifications/00000000-0000-0000-0000-000000000001/read", NotificationPrivileges.UpdateOwn },
        { "POST", "/api/me/notifications/read-all", NotificationPrivileges.UpdateOwn },
        { "GET", "/api/me/notification-preferences", NotificationPrivileges.PreferenceReadOwn },
        { "PUT", "/api/me/notification-preferences/workforce.vacation-decision.requester/Push", NotificationPrivileges.PreferenceUpdateOwn },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Anonymous_callers_are_unauthorized_and_people_without_the_privilege_are_forbidden(string method, string url, string privilege)
    {
        await using var api = await StartAsync(postgres);
        var onlyOther = NotificationPrivileges.All.Where(p => p != privilege).Select(p => new TestGrant(p)).ToArray();
        api.Services.GetRequiredService<IPermissionCache>().Grant(Stranger, onlyOther);
        object? body = method == "PUT" ? new SetPreferenceRequest(false) : null;

        (await api.SendAsync(new HttpMethod(method), url, null, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.SendAsync(new HttpMethod(method), url, Stranger, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(new HttpMethod(method), url, Guid.NewGuid(), body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // no permission copy: fail closed
    }

    [Fact]
    public async Task A_service_token_cannot_read_a_persons_notifications()
    {
        await using var api = await StartAsync(postgres);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me/notifications").AsService("Workforce.AbsenceDetails.Read");

        using var response = await api.Http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
