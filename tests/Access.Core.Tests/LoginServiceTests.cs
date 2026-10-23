using Access.Core.Login;
using Access.Core.People;
using Microsoft.Extensions.Time.Testing;
using static Access.Core.Tests.World;

namespace Access.Core.Tests;

/// <summary>Personnel number + BC PIN: BC decides; hash and encrypted PIN enable the offline login while BC is down.</summary>
public class LoginServiceTests
{
    private const string Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="; // test key only
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 29, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeBc _bc = new();
    private readonly FakeCredentials _credentials = new();
    private readonly List<Person> _people;
    private readonly LoginThrottle _throttle;

    public LoginServiceTests()
    {
        _people = new World().People;
        _throttle = new LoginThrottle(_time, new LoginThrottleOptions { MaxFailures = 3, Window = TimeSpan.FromMinutes(15) });
        _bc.Pins["10042"] = "4711";
        _bc.Pins["10001"] = "1234";
    }

    private LoginService Service(LoginOptions? options = null) => new(
        new FakeDirectory(_people), _bc, _credentials, new PinHasher(1000), new PinProtector(Key),
        _throttle, _time, options ?? new LoginOptions { MaxFailuresPerAccount = 3 }, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));

    [Fact]
    public async Task Valid_pin_logs_in_and_stores_hash_and_encrypted_pin()
    {
        var result = await Service().LoginAsync("10042", "4711", "client-a", CancellationToken.None);

        result.ShouldBe(LoginResult.Success(Erika));
        var stored = _credentials.Rows[Erika];
        stored.PinHash.ShouldNotContain("4711");
        stored.EncryptedPin.ShouldNotBeNull().ShouldNotContain("4711");
        new PinProtector(Key).Unprotect(stored.EncryptedPin).ShouldBe("4711");
        stored.VerifiedAt.ShouldBe(_time.GetUtcNow());
    }

    [Fact]
    public async Task Wrong_pin_is_refused_and_nothing_is_stored()
    {
        var result = await Service().LoginAsync("10042", "0000", "client-a", CancellationToken.None);

        result.ShouldBe(LoginResult.InvalidCredentials);
        _credentials.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("99999", "4711")] // unknown personnel number
    [InlineData("10042", "")]
    [InlineData("", "4711")]
    [InlineData("  ", "  ")]
    public async Task Unknown_person_and_empty_values_look_the_same_and_never_reach_bc(string number, string pin)
    {
        var result = await Service().LoginAsync(number, pin, "client-a", CancellationToken.None);

        result.ShouldBe(LoginResult.InvalidCredentials);
        _bc.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Person_who_has_left_cannot_log_in_even_with_a_valid_pin()
    {
        _people[0] = _people[0] with { ExitDate = new DateOnly(2026, 3, 28) };

        (await Service().LoginAsync("10042", "4711", "client-a", CancellationToken.None)).ShouldBe(LoginResult.InvalidCredentials);

        _bc.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Person_with_a_known_exit_date_in_the_future_can_still_log_in()
    {
        _people[0] = _people[0] with { ExitDate = new DateOnly(2026, 3, 29) };

        (await Service().LoginAsync("10042", "4711", "client-a", CancellationToken.None)).ShouldBe(LoginResult.Success(Erika));
    }

    [Fact]
    public async Task Bc_outage_falls_back_to_the_last_verified_pin()
    {
        await Service().LoginAsync("10042", "4711", "client-a", CancellationToken.None);
        _bc.Down = true;
        _time.Advance(TimeSpan.FromDays(3));

        (await Service().LoginAsync("10042", "4711", "client-a", CancellationToken.None)).ShouldBe(LoginResult.Success(Erika));
        (await Service().LoginAsync("10042", "0000", "client-a", CancellationToken.None)).ShouldBe(LoginResult.InvalidCredentials);
    }

    [Fact]
    public async Task Offline_login_expires_after_the_configured_period()
    {
        var options = new LoginOptions { OfflineValidity = TimeSpan.FromDays(30), MaxFailuresPerAccount = 3 };
        await Service(options).LoginAsync("10042", "4711", "client-a", CancellationToken.None);
        _bc.Down = true;

        _time.Advance(TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1));
        (await Service(options).LoginAsync("10042", "4711", "client-a", CancellationToken.None)).ShouldBe(LoginResult.Success(Erika));
        _time.Advance(TimeSpan.FromSeconds(2));
        (await Service(options).LoginAsync("10042", "4711", "client-a", CancellationToken.None)).ShouldBe(LoginResult.Unavailable);
    }

    [Fact]
    public async Task Bc_outage_without_a_stored_pin_means_unavailable_not_invalid()
    {
        _bc.Down = true;

        (await Service().LoginAsync("10042", "4711", "client-a", CancellationToken.None)).ShouldBe(LoginResult.Unavailable);
    }

    [Fact]
    public async Task Offline_login_is_disabled_by_configuration()
    {
        var options = new LoginOptions { OfflineValidity = TimeSpan.Zero };
        await Service(options).LoginAsync("10042", "4711", "client-a", CancellationToken.None);
        _bc.Down = true;

        (await Service(options).LoginAsync("10042", "4711", "client-a", CancellationToken.None)).ShouldBe(LoginResult.Unavailable);
    }

    [Fact]
    public async Task A_pin_changed_in_bc_replaces_the_stored_one_and_the_old_one_stops_working_offline()
    {
        await Service().LoginAsync("10042", "4711", "client-a", CancellationToken.None);
        _bc.Pins["10042"] = "9999";
        await Service().LoginAsync("10042", "9999", "client-a", CancellationToken.None);
        _bc.Down = true;

        (await Service().LoginAsync("10042", "9999", "client-a", CancellationToken.None)).ShouldBe(LoginResult.Success(Erika));
        (await Service().LoginAsync("10042", "4711", "client-a", CancellationToken.None)).ShouldBe(LoginResult.InvalidCredentials);
        new PinProtector(Key).Unprotect(_credentials.Rows[Erika].EncryptedPin!).ShouldBe("9999");
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_even_with_the_right_pin_and_without_asking_bc()
    {
        for (var i = 0; i < 3; i++)
        {
            await Service().LoginAsync("10042", "0000", $"client-{i}", CancellationToken.None);
        }

        _bc.Calls.Clear();
        var locked = await Service().LoginAsync("10042", "4711", "client-x", CancellationToken.None);

        locked.Status.ShouldBe(LoginStatus.Locked);
        locked.LockedUntil.ShouldBe(_time.GetUtcNow().AddMinutes(15));
        _bc.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Failures_of_unknown_personnel_numbers_count_too_so_numbers_cannot_be_probed()
    {
        for (var i = 0; i < 3; i++)
        {
            await Service().LoginAsync("99999", "1111", "client-a", CancellationToken.None);
        }

        (await Service().LoginAsync("99999", "1111", "client-a", CancellationToken.None)).Status.ShouldBe(LoginStatus.Locked);
    }

    [Fact]
    public async Task One_client_cannot_guess_across_many_personnel_numbers()
    {
        var service = Service();
        for (var i = 0; i < 20; i++)
        {
            await service.LoginAsync($"2{i:0000}", "1111", "attacker", CancellationToken.None);
        }

        (await service.LoginAsync("10042", "4711", "attacker", CancellationToken.None)).Status.ShouldBe(LoginStatus.Locked);
        (await service.LoginAsync("10042", "4711", "someone-else", CancellationToken.None)).ShouldBe(LoginResult.Success(Erika));
    }

    [Fact]
    public async Task Success_resets_the_failures_of_the_account()
    {
        await Service().LoginAsync("10042", "0000", "c", CancellationToken.None);
        await Service().LoginAsync("10042", "0000", "c", CancellationToken.None);
        await Service().LoginAsync("10042", "4711", "c", CancellationToken.None);
        await Service().LoginAsync("10042", "0000", "c", CancellationToken.None);

        (await Service().LoginAsync("10042", "4711", "c", CancellationToken.None)).ShouldBe(LoginResult.Success(Erika));
    }

    [Fact]
    public async Task Without_encryption_key_only_the_hash_is_stored()
    {
        var service = new LoginService(
            new FakeDirectory(_people), _bc, _credentials, new PinHasher(1000), new PinProtector(null),
            new LoginThrottle(_time, new LoginThrottleOptions()), _time, new LoginOptions(), TimeZoneInfo.Utc);

        await service.LoginAsync("10042", "4711", "c", CancellationToken.None);

        _credentials.Rows[Erika].EncryptedPin.ShouldBeNull();
        _credentials.Rows[Erika].PinHash.ShouldNotBeEmpty();
    }
}
