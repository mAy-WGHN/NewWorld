using Access.Core.Login;
using Microsoft.Extensions.Time.Testing;

namespace Access.Core.Tests;

public class LoginThrottleTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));

    private LoginThrottle Throttle() => new(_time, new LoginThrottleOptions { MaxFailures = 3, Window = TimeSpan.FromMinutes(15) });

    [Fact]
    public void Locks_after_the_maximum_number_of_failures_and_reports_when_it_ends()
    {
        var throttle = Throttle();
        for (var i = 0; i < 3; i++)
        {
            throttle.IsLocked("10042", out _).ShouldBeFalse();
            throttle.RecordFailure("10042");
        }

        throttle.IsLocked("10042", out var until).ShouldBeTrue();
        until.ShouldBe(_time.GetUtcNow().AddMinutes(15));
    }

    [Fact]
    public void Lock_ends_when_the_failures_leave_the_window()
    {
        var throttle = Throttle();
        for (var i = 0; i < 3; i++)
        {
            throttle.RecordFailure("10042");
        }

        _time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1)); // crosses the change to winter time on this night

        throttle.IsLocked("10042", out _).ShouldBeFalse();
    }

    [Fact]
    public void Success_resets_the_failures_of_that_key_only()
    {
        var throttle = Throttle();
        throttle.RecordFailure("10042");
        throttle.RecordFailure("10042");
        throttle.RecordFailure("10043");

        throttle.RecordSuccess("10042");
        throttle.RecordFailure("10042");

        throttle.IsLocked("10042", out _).ShouldBeFalse();
        throttle.Failures("10042").ShouldBe(1);
        throttle.Failures("10043").ShouldBe(1);
    }

    [Fact]
    public void Keys_are_independent() 
    {
        var throttle = Throttle();
        for (var i = 0; i < 3; i++)
        {
            throttle.RecordFailure("10042");
        }

        throttle.IsLocked("10043", out _).ShouldBeFalse();
    }
}
