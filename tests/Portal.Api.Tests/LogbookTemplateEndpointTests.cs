using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core;
using Portal.Core.Logbook;
using Portal.Infrastructure;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// The template of the weekly report through the real API and database (decision 49): a suggestion applies until the first version is saved, every change
/// of the content is a new version, old versions stay readable (reports keep their version), and only the holders of the template privilege change it.
/// Clock: 2026-10-23, 12:00 UTC.
/// </summary>
public sealed class LogbookTemplateEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static object Body(string title = "Gesamtlage", string firstOption = "Gut", bool required = true, Guid? sectionId = null, Guid? optionId = null) => new
    {
        sections = new object[]
        {
            new
            {
                id = sectionId, title, hint = "Kurz", kind = "singleChoice", required,
                options = new object[] { new { id = optionId, label = firstOption }, new { id = (Guid?)null, label = "Schlecht" } },
            },
            new { id = (Guid?)null, title = "Anmerkungen", hint = (string?)null, kind = "freeText", required = false, options = Array.Empty<object>() },
        },
    };

    private static PortalApi WithPrivileges(PortalApi api)
    {
        api.Grant(Ben, TestGrant.Everywhere(PortalPrivileges.LogbookManageTemplate));
        api.Grant(Erika, TestGrant.Everywhere(PortalPrivileges.LogbookWrite));
        api.Grant(Revisor, TestGrant.Everywhere(PortalPrivileges.LogbookReview));
        return api;
    }

    private static async Task<JsonElement> GetAsync(PortalApi api, string url, Guid person)
    {
        using var response = await api.SendAsync(HttpMethod.Get, url, person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private static async Task<JsonElement> PutAsync(PortalApi api, object body)
    {
        using var response = await api.SendAsync(HttpMethod.Put, "/api/logbook/template", Ben, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private static string[] Titles(JsonElement template) => [.. template.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("title").GetString()!)];

    // ------------------------------------------------------------------ reading

    [Fact]
    public async Task Until_a_version_is_saved_the_suggestion_applies_as_version_0()
    {
        await using var api = WithPrivileges(await StartAsync(postgres));

        var template = await GetAsync(api, "/api/logbook/template", Erika);

        template.GetProperty("version").GetInt32().ShouldBe(0);
        template.GetProperty("sections").GetArrayLength().ShouldBe(LogbookTemplate.Default.Sections.Count);
        var first = template.GetProperty("sections")[0];
        first.GetProperty("id").GetGuid().ShouldBe(LogbookTemplate.Default.Sections[0].Id);
        first.GetProperty("kind").GetString().ShouldBe("singleChoice");
        first.GetProperty("required").GetBoolean().ShouldBeTrue();
        first.GetProperty("options").GetArrayLength().ShouldBe(3);
        (await GetAsync(api, "/api/logbook/template/0", Revisor)).GetProperty("version").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Whoever_fills_reads_or_maintains_may_read_the_template_and_nobody_else()
    {
        await using var api = WithPrivileges(await StartAsync(postgres));
        api.Grant(It, TestGrant.Everywhere(PortalPrivileges.FaqRead)); // other privileges do not help

        foreach (var person in new[] { Erika, Revisor, Ben, Hanna })
        {
            (await api.SendAsync(HttpMethod.Get, "/api/logbook/template", person)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await api.SendAsync(HttpMethod.Get, "/api/logbook/template", It)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, "/api/logbook/template/0", It)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, "/api/logbook/template", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ saving and versions

    [Fact]
    public async Task The_first_save_makes_version_1_with_ids_for_everything_that_had_none_and_it_becomes_the_current_one()
    {
        await using var api = WithPrivileges(await StartAsync(postgres));

        var saved = await PutAsync(api, Body());

        saved.GetProperty("version").GetInt32().ShouldBe(1);
        Titles(saved).ShouldBe(["Gesamtlage", "Anmerkungen"]);
        var ids = saved.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("id").GetGuid())
            .Concat(saved.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("options").EnumerateArray()).Select(o => o.GetProperty("id").GetGuid())).ToList();
        ids.ShouldAllBe(id => id != Guid.Empty);
        ids.Distinct().Count().ShouldBe(ids.Count);
        (await GetAsync(api, "/api/logbook/template", Erika)).GetProperty("version").GetInt32().ShouldBe(1);
        saved.GetProperty("publishedAt").GetDateTimeOffset().ShouldBe(Start);
    }

    [Fact]
    public async Task The_same_content_is_not_a_new_version_and_a_change_is_and_the_old_version_stays_readable()
    {
        await using var api = WithPrivileges(await StartAsync(postgres));
        var first = await PutAsync(api, Body());
        var sectionId = first.GetProperty("sections")[0].GetProperty("id").GetGuid();
        var optionId = first.GetProperty("sections")[0].GetProperty("options")[0].GetProperty("id").GetGuid();
        var secondOptionId = first.GetProperty("sections")[0].GetProperty("options")[1].GetProperty("id").GetGuid();
        var textId = first.GetProperty("sections")[1].GetProperty("id").GetGuid();

        object Same(string label) => new
        {
            sections = new object[]
            {
                new { id = (Guid?)sectionId, title = "Gesamtlage", hint = "Kurz", kind = "singleChoice", required = true, options = new object[] { new { id = (Guid?)optionId, label }, new { id = (Guid?)secondOptionId, label = "Schlecht" } } },
                new { id = (Guid?)textId, title = "Anmerkungen", hint = (string?)null, kind = "freeText", required = false, options = Array.Empty<object>() },
            },
        };

        (await PutAsync(api, Same("Gut"))).GetProperty("version").GetInt32().ShouldBe(1); // nothing changed
        (await PutAsync(api, Same("  Gut  "))).GetProperty("version").GetInt32().ShouldBe(1); // spaces around do not count

        var second = await PutAsync(api, Same("Sehr gut"));
        second.GetProperty("version").GetInt32().ShouldBe(2);
        second.GetProperty("sections")[0].GetProperty("options")[0].GetProperty("id").GetGuid().ShouldBe(optionId); // the id stays, so old answers keep their meaning
        (await GetAsync(api, "/api/logbook/template/1", Erika)).GetProperty("sections")[0].GetProperty("options")[0].GetProperty("label").GetString().ShouldBe("Gut");
        (await GetAsync(api, "/api/logbook/template/2", Erika)).GetProperty("sections")[0].GetProperty("options")[0].GetProperty("label").GetString().ShouldBe("Sehr gut");
        (await GetAsync(api, "/api/logbook/template", Erika)).GetProperty("version").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task An_unknown_version_is_not_found()
    {
        await using var api = WithPrivileges(await StartAsync(postgres));
        await PutAsync(api, Body());

        (await api.SendAsync(HttpMethod.Get, "/api/logbook/template/7", Erika)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Get, "/api/logbook/template/-1", Erika)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_wrong_template_is_refused_with_the_field_and_nothing_is_saved()
    {
        await using var api = WithPrivileges(await StartAsync(postgres));
        var noSections = new { sections = Array.Empty<object>() };
        var oneOption = new { sections = new object[] { new { id = (Guid?)null, title = "A", hint = (string?)null, kind = "singleChoice", required = false, options = new object[] { new { id = (Guid?)null, label = "Nur eine" } } } } };
        var unknownKind = new { sections = new object[] { new { id = (Guid?)null, title = "A", hint = (string?)null, kind = "slider", required = false, options = Array.Empty<object>() } } };
        var noTitle = new { sections = new object[] { new { id = (Guid?)null, title = " ", hint = (string?)null, kind = "freeText", required = false, options = Array.Empty<object>() } } };

        foreach (var body in new object[] { noSections, oneOption, unknownKind, noTitle, new { sections = (object?)null } })
        {
            using var response = await api.SendAsync(HttpMethod.Put, "/api/logbook/template", Ben, body);
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await ErrorAsync(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ShouldContain("sections");
        }

        (await GetAsync(api, "/api/logbook/template", Erika)).GetProperty("version").GetInt32().ShouldBe(0);
        using var scope = api.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<PortalDbContext>().LogbookTemplates.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Only_the_holders_of_the_template_privilege_change_it()
    {
        await using var api = WithPrivileges(await StartAsync(postgres));

        foreach (var person in new Guid?[] { Erika, Revisor, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Put, "/api/logbook/template", person, Body())).StatusCode.ShouldBe(expected);
        }

        (await GetAsync(api, "/api/logbook/template", Erika)).GetProperty("version").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Two_changes_at_the_same_time_make_two_versions_or_one_refusal_never_a_broken_state()
    {
        await using var api = WithPrivileges(await StartAsync(postgres));

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
        {
            using var response = await api.SendAsync(HttpMethod.Put, "/api/logbook/template", Ben, Body(title: $"Titel {i}"));
            return response.StatusCode;
        }));

        results.ShouldAllBe(s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
        using var scope = api.Services.CreateScope();
        var versions = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().LogbookTemplates.Select(t => t.Version).OrderBy(v => v).ToListAsync();
        versions.ShouldBe([.. Enumerable.Range(1, versions.Count)]); // no gaps, no duplicates
        versions.Count.ShouldBe(results.Count(s => s == HttpStatusCode.OK));
    }
}
