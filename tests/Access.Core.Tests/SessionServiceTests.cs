using Access.Core.Login;
using Access.Core.People;
using Microsoft.Extensions.Time.Testing;
using static Access.Core.Tests.World;

namespace Access.Core.Tests;

public class SessionServiceTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2028, 2, 29, 23, 0, 0, TimeSpan.Zero));
    private readonly FakeRefreshTokens _tokens = new();
    private readonly List<Person> _people = new World().People;

    private SessionService Service() => new(_tokens, new FakeDirectory(_people), _time, new LoginOptions { RefreshTokenLifetime = TimeSpan.FromDays(7) },
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));

    [Fact]
    public async Task Issued_token_is_stored_only_as_hash()
    {
        var token = await Service().StartAsync(Erika, CancellationToken.None);

        token.Length.ShouldBeGreaterThan(40);
        _tokens.Rows.ShouldHaveSingleItem().TokenHash.ShouldNotContain(token);
        _tokens.Rows[0].ExpiresAt.ShouldBe(_time.GetUtcNow().AddDays(7));
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_the_old_one_is_used_up()
    {
        var first = await Service().StartAsync(Erika, CancellationToken.None);

        var refreshed = await Service().RefreshAsync(first, CancellationToken.None);

        refreshed.Status.ShouldBe(RefreshStatus.Refreshed);
        refreshed.EmployeeId.ShouldBe(Erika);
        refreshed.NewToken.ShouldNotBeNull().ShouldNotBe(first);
    }

    [Fact]
    public async Task Reusing_a_used_token_revokes_all_sessions_of_the_person()
    {
        var first = await Service().StartAsync(Erika, CancellationToken.None);
        var second = (await Service().RefreshAsync(first, CancellationToken.None)).NewToken!;

        var reuse = await Service().RefreshAsync(first, CancellationToken.None);

        reuse.Status.ShouldBe(RefreshStatus.Rejected);
        (await Service().RefreshAsync(second, CancellationToken.None)).Status.ShouldBe(RefreshStatus.Rejected);
        _tokens.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-token")]
    public async Task Unknown_tokens_are_rejected(string token) =>
        (await Service().RefreshAsync(token, CancellationToken.None)).Status.ShouldBe(RefreshStatus.Rejected);

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var token = await Service().StartAsync(Erika, CancellationToken.None);
        _time.Advance(TimeSpan.FromDays(7));

        (await Service().RefreshAsync(token, CancellationToken.None)).Status.ShouldBe(RefreshStatus.Rejected);
    }

    [Fact]
    public async Task Person_who_has_left_cannot_refresh_after_the_end_of_the_exit_date()
    {
        var token = await Service().StartAsync(Erika, CancellationToken.None); // 2028-03-01 00:00 local
        _people[0] = _people[0] with { ExitDate = new DateOnly(2028, 3, 1) };

        _time.Advance(TimeSpan.FromHours(23));
        (await Service().RefreshAsync(token, CancellationToken.None)).Status.ShouldBe(RefreshStatus.Refreshed);
    }

    [Fact]
    public async Task Refresh_after_exit_is_rejected_and_revokes()
    {
        var token = await Service().StartAsync(Erika, CancellationToken.None);
        _people[0] = _people[0] with { ExitDate = new DateOnly(2028, 3, 1) };

        _time.Advance(TimeSpan.FromHours(25)); // after local midnight of 2 March

        (await Service().RefreshAsync(token, CancellationToken.None)).Status.ShouldBe(RefreshStatus.Rejected);
        _tokens.Rows.ShouldBeEmpty();
    }
}
