using System.Net;
using System.Net.Http.Json;
using Access.Api;
using Access.Core.Acl;
using Shared.Testing;
using static Access.Api.Tests.AccessApi;

namespace Access.Api.Tests;

/// <summary>Finding a person by name or personnel number for the administration: words, no case, reach, ids, and who may ask at all.</summary>
public sealed class PeopleSearchTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static Func<HttpRequestMessage, HttpRequestMessage> As(Guid person) => r => r.AsUser(person);

    private static string Search(string query) => $"/api/people?query={Uri.EscapeDataString(query)}";

    [Fact]
    public async Task A_person_is_found_by_a_part_of_the_name_in_any_case()
    {
        await using var api = await StartAsync(postgres);

        var found = await api.GetAsync<List<PersonDto>>(Search("MUSTER"), As(Admin));

        found.Select(p => p.EmployeeId).ShouldBe([Erika, Max], ignoreOrder: true);
        var erika = found.Single(p => p.EmployeeId == Erika);
        (erika.PersonnelNumber, erika.Name, erika.HomeStoreId).ShouldBe(("10042", "Erika Mustermann", F017));
    }

    [Fact]
    public async Task A_person_is_found_by_the_personnel_number()
    {
        await using var api = await StartAsync(postgres);

        (await api.GetAsync<List<PersonDto>>(Search("10042"), As(Admin))).Select(p => p.EmployeeId).ShouldBe([Erika]);
        (await api.GetAsync<List<PersonDto>>(Search("1000"), As(Admin))).Select(p => p.EmployeeId).ShouldBe([Max, Tom], ignoreOrder: true);
    }

    [Fact]
    public async Task Every_word_has_to_match_name_or_number()
    {
        await using var api = await StartAsync(postgres);

        (await api.GetAsync<List<PersonDto>>(Search("erika must"), As(Admin))).Select(p => p.EmployeeId).ShouldBe([Erika]);
        (await api.GetAsync<List<PersonDto>>(Search("muster 10001"), As(Admin))).Select(p => p.EmployeeId).ShouldBe([Max]);
        (await api.GetAsync<List<PersonDto>>(Search("erika tester"), As(Admin))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Wildcard_characters_are_taken_literally()
    {
        await using var api = await StartAsync(postgres);

        (await api.GetAsync<List<PersonDto>>(Search("%%"), As(Admin))).ShouldBeEmpty();
        (await api.GetAsync<List<PersonDto>>(Search("e_"), As(Admin))).ShouldBeEmpty();
        (await api.GetAsync<List<PersonDto>>(Search(@"\\"), As(Admin))).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_query_needs_at_least_two_characters()
    {
        await using var api = await StartAsync(postgres);

        foreach (var query in new[] { "", " ", "e", " e " })
        {
            using var response = await api.SendAsync(HttpMethod.Get, Search(query), As(Admin));
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, $"query '{query}'");
        }
    }

    [Fact]
    public async Task The_number_of_results_is_limited()
    {
        await using var api = await StartAsync(postgres);

        (await api.GetAsync<List<PersonDto>>(Search("te") + "&take=1", As(Admin))).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Ids_give_exactly_those_people_and_skip_unknown_or_broken_ones()
    {
        await using var api = await StartAsync(postgres);

        var found = await api.GetAsync<List<PersonDto>>($"/api/people?ids={Erika},{Tom},{Guid.NewGuid()},not-a-guid", As(Admin));

        found.Select(p => p.EmployeeId).ShouldBe([Erika, Tom], ignoreOrder: true);
    }

    [Fact]
    public async Task Someone_who_may_not_administrate_gets_nothing_and_a_stranger_is_turned_away()
    {
        await using var api = await StartAsync(postgres);

        (await api.SendAsync(HttpMethod.Get, Search("muster"), As(Erika))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, $"/api/people?ids={Erika}", As(Erika))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, Search("muster"), null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Only_people_in_the_reach_of_the_privilege_are_offered()
    {
        await using var api = await StartAsync(postgres);
        var roleId = await api.CreateAsync("/api/roles", new CreateRoleRequest("Zuweisungen ansehen", [AccessPrivileges.AssignmentRead]), As(Admin));
        await api.CreateAsync($"/api/people/{Max}/assignments", new CreateAssignmentRequest(roleId, false, [], true, null, null), As(Admin)); // his own store F017

        var found = await api.GetAsync<List<PersonDto>>(Search("te"), As(Max));
        var byId = await api.GetAsync<List<PersonDto>>($"/api/people?ids={Erika},{Tom}", As(Max));

        found.Select(p => p.EmployeeId).ShouldBe([Erika, Max], ignoreOrder: true); // Tom works in F018
        byId.Select(p => p.EmployeeId).ShouldBe([Erika]);
    }
}
