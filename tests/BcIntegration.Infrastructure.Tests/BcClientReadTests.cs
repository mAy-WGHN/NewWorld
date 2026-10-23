using BcIntegration.Core.Errors;

namespace BcIntegration.Infrastructure.Tests;

/// <summary>Reading master data from the BC fake: success, pagination, filters, throttling, failures, tokens.</summary>
public class BcClientReadTests
{
    [Fact]
    public async Task Reads_employees_and_normalises_empty_bc_values()
    {
        await using var bc = BcTestHost.Start("employees-success");

        var employees = await bc.Client.GetEmployeesModifiedSinceAsync(null, CancellationToken.None);

        employees.Count.ShouldBe(2);
        var erika = employees.Single(e => e.PersonnelNumber == "10042");
        erika.Name.ShouldBe("Erika Mustermann");
        erika.EntryDate.ShouldBe(new DateOnly(2024, 2, 29));
        erika.ExitDate.ShouldBeNull(); // BC sends 0001-01-01 for "no date"
        erika.StoreNumber.ShouldBe("F017");
        erika.ContractedHoursPerWeek.ShouldBe(30.5m);
        employees.Single(e => e.PersonnelNumber == "10043").StoreNumber.ShouldBeNull();
    }

    [Fact]
    public async Task Empty_result_is_an_empty_list()
    {
        await using var bc = BcTestHost.Start("employees-empty");

        (await bc.Client.GetEmployeesModifiedSinceAsync(null, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Follows_next_links_across_pages()
    {
        await using var bc = BcTestHost.Start("employees-pagination");

        var employees = await bc.Client.GetEmployeesModifiedSinceAsync(null, CancellationToken.None);

        employees.Select(e => e.PersonnelNumber).ShouldBe(["10042", "10043"]);
        bc.RequestCount("GET").ShouldBe(2);
    }

    [Fact]
    public async Task Next_link_pointing_back_stops_with_a_technical_failure_instead_of_looping()
    {
        await using var bc = BcTestHost.Start("employees-pagination-loop");

        var ex = await Should.ThrowAsync<BcTechnicalFailureException>(() => bc.Client.GetEmployeesModifiedSinceAsync(null, CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(10));

        ex.Reason.ShouldBe("BcPaginationLoop");
    }

    [Fact]
    public async Task Unreadable_answer_is_a_technical_failure()
    {
        await using var bc = BcTestHost.Start("employees-invalid-json");

        var ex = await Should.ThrowAsync<BcTechnicalFailureException>(() => bc.Client.GetEmployeesModifiedSinceAsync(null, CancellationToken.None));

        ex.Reason.ShouldBe("BcInvalidResponse");
    }

    [Fact]
    public async Task Filters_on_last_modified_since_the_given_time_in_utc()
    {
        await using var bc = BcTestHost.Start("employees-empty");

        await bc.Client.GetEmployeesModifiedSinceAsync(new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(1)), CancellationToken.None);

        bc.Server.LogEntries.Single().RequestMessage!.Query!["$filter"].ToString().ShouldBe("lastModifiedDateTime gt 2026-10-25T01:30:00.000Z");
    }

    [Fact]
    public async Task Throttled_request_waits_for_retry_after_and_succeeds()
    {
        await using var bc = BcTestHost.Start("employees-throttled");

        var employees = await bc.Client.GetEmployeesModifiedSinceAsync(null, CancellationToken.None);

        employees.ShouldHaveSingleItem();
        var times = bc.Server.LogEntries.Select(e => e.RequestMessage!.DateTime).OrderBy(t => t).ToList();
        times.Count.ShouldBe(2);
        (times[1] - times[0]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task Server_errors_are_retried_and_then_reported_as_transient()
    {
        await using var bc = BcTestHost.Start("employees-server-error");

        var ex = await Should.ThrowAsync<BcTransientFailureException>(() => bc.Client.GetEmployeesModifiedSinceAsync(null, CancellationToken.None));

        ex.Reason.ShouldBe("Http500");
        bc.RequestCount("GET").ShouldBe(3); // first attempt + 2 retries
        ex.Message.ShouldNotContain("Erika");
    }

    [Fact]
    public async Task Slow_answer_times_out_and_is_reported_as_transient()
    {
        await using var bc = BcTestHost.Start("employees-timeout", new Dictionary<string, string?>
        {
            ["Bc:Resilience:AttemptTimeout"] = "00:00:00.300",
            ["Bc:Resilience:MaxRetries"] = "1",
        });

        var ex = await Should.ThrowAsync<BcTransientFailureException>(() => bc.Client.GetEmployeesModifiedSinceAsync(null, CancellationToken.None));

        ex.Reason.ShouldBe("Timeout");
    }

    [Fact]
    public async Task Expired_token_is_refreshed_once()
    {
        await using var bc = BcTestHost.Start("employees-expired-token");

        (await bc.Client.GetEmployeesModifiedSinceAsync(null, CancellationToken.None)).ShouldHaveSingleItem();

        bc.Credential.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Rejected_credentials_are_a_permanent_technical_failure()
    {
        await using var bc = BcTestHost.Start("employees-unauthorized");

        var ex = await Should.ThrowAsync<BcTechnicalFailureException>(() => bc.Client.GetEmployeesModifiedSinceAsync(null, CancellationToken.None));

        ex.Reason.ShouldBe("BcUnauthorized");
        bc.RequestCount("GET").ShouldBe(2); // one refresh, no retries
    }

    [Fact]
    public async Task Missing_configuration_for_a_custom_api_page_is_reported_not_guessed()
    {
        await using var bc = BcTestHost.Start("employees-empty", new Dictionary<string, string?> { ["Bc:StoresPath"] = "" });

        var ex = await Should.ThrowAsync<BcTechnicalFailureException>(() => bc.Client.GetStoresModifiedSinceAsync(null, CancellationToken.None));

        ex.Reason.ShouldBe("BcNotConfigured:StoresPath");
        bc.Server.LogEntries.ShouldBeEmpty();
    }
}
