using Microsoft.Extensions.Time.Testing;
using Notifications.Contracts;
using Workforce.Core.Absences;
using Workforce.Core.WorkingTime;

namespace Workforce.Core.Tests;

/// <summary>
/// Somebody has left and the working time account still holds a balance: HR is reminded once, the day after the exit, to pay it out (a HR action).
/// The reminder carries no data; nothing is booked automatically.
/// </summary>
public class WorkingTimePayoutReminderTests
{
    private static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid Hanna = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid Hr2 = Guid.Parse("00000000-0000-0000-0000-0000000000d2");
    private static readonly Guid Store = Guid.Parse("00000000-0000-0000-0000-000000000017");

    private readonly FakeWorkingTimeStore _store = new();
    private readonly FakeStore _absences = new();
    private readonly FakePeople _people = new();
    private readonly FakeEmployment _employment = new();
    private readonly FakeResponsibilities _responsibilities = new();
    private readonly RecordingMessages _messages = new();
    private FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));

    public WorkingTimePayoutReminderTests()
    {
        _people.People[Erika] = new PersonInfo(Erika, "Erika Mustermann", Store, new DateOnly(2026, 10, 20));
        _responsibilities.PayoutHandlers[Erika] = [Hanna];
        _store.Exited.Add(new ExitedAccount(Erika, new DateOnly(2026, 10, 20)));
    }

    private WorkingTimeService Service() => new(_store, _absences, _people, _employment, _responsibilities, _messages, _time, new WorkforceOptions());

    private static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    private void Balance(decimal hours, string date = "2026-10-01") =>
        _store.Bookings.Add(new TimeBooking { Id = Guid.NewGuid(), EmployeeId = Erika, Kind = BookingKind.Correction, Hours = hours, Date = D(date), CreatedBy = Hanna, CreatedAt = _time.GetUtcNow() });

    [Fact]
    public async Task The_day_after_the_exit_HR_is_reminded_without_any_data_and_nothing_is_booked()
    {
        Balance(12);

        (await Service().RemindPayoutAfterExitAsync(CancellationToken.None)).ShouldBe(1);

        var reminder = _messages.Of<SendNotification>().Single();
        reminder.TemplateKey.ShouldBe(WorkingTimeService.PayoutReminderTemplate);
        reminder.RecipientEmployeeIds.ShouldBe([Hanna]);
        reminder.Channels.ShouldBe([NotificationChannel.InApp, NotificationChannel.Push], ignoreOrder: true);
        reminder.Data.ShouldBeEmpty();
        reminder.DeepLink.ShouldBe($"/employees/{Erika}/working-time");
        _store.Bookings.Count.ShouldBe(1); // only the one set up: nothing was paid out
        _store.Checks[Erika].ShouldBeTrue();
    }

    [Fact]
    public async Task All_responsible_persons_are_told_but_never_the_person_who_left()
    {
        Balance(12);
        _responsibilities.PayoutHandlers[Erika] = [Hanna, Hr2, Erika];

        await Service().RemindPayoutAfterExitAsync(CancellationToken.None);

        _messages.Of<SendNotification>().Single().RecipientEmployeeIds.ShouldBe([Hanna, Hr2], ignoreOrder: true);
    }

    [Theory]
    [InlineData("2026-10-20T10:00:00Z")]   // the exit day itself
    [InlineData("2026-10-19T10:00:00Z")]   // before
    public async Task Not_before_the_exit_day_is_over(string now)
    {
        Balance(12);
        _time = new FakeTimeProvider(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));

        (await Service().RemindPayoutAfterExitAsync(CancellationToken.None)).ShouldBe(0);

        _messages.Sent.ShouldBeEmpty();
        _store.Checks.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2026-10-24", "2026-10-24T21:59:00Z", 0)]   // 23:59 local on the exit day (summer time ends the night after)
    [InlineData("2026-10-24", "2026-10-24T22:00:00Z", 1)]   // midnight local: the day after
    [InlineData("2028-02-29", "2028-02-29T22:59:00Z", 0)]   // leap day
    [InlineData("2028-02-29", "2028-02-29T23:00:00Z", 1)]
    [InlineData("2026-12-31", "2026-12-31T22:59:00Z", 0)]   // year end
    [InlineData("2026-12-31", "2026-12-31T23:00:00Z", 1)]
    public async Task The_day_changes_at_local_midnight_also_on_the_special_days(string exit, string now, int expected)
    {
        _people.People[Erika] = _people.People[Erika] with { ExitDate = D(exit) };
        _store.Exited.Clear();
        _store.Exited.Add(new ExitedAccount(Erika, D(exit)));
        Balance(5, "2026-01-02");
        _time = new FakeTimeProvider(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));

        (await Service().RemindPayoutAfterExitAsync(CancellationToken.None)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public async Task Without_a_positive_balance_there_is_nothing_to_pay_and_the_person_is_not_looked_at_again(double hours)
    {
        Balance((decimal)hours);

        (await Service().RemindPayoutAfterExitAsync(CancellationToken.None)).ShouldBe(0);

        _messages.Sent.ShouldBeEmpty();
        _store.Checks[Erika].ShouldBeFalse(); // checked, not reminded
        (await Service().RemindPayoutAfterExitAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Fact]
    public async Task A_balance_that_was_paid_out_at_the_exit_needs_no_reminder()
    {
        Balance(12);
        _store.Bookings.Add(new TimeBooking { Id = Guid.NewGuid(), EmployeeId = Erika, Kind = BookingKind.PayoutAtExit, Hours = -12, Date = D("2026-10-20"), CreatedBy = Hanna, CreatedAt = _time.GetUtcNow() });

        (await Service().RemindPayoutAfterExitAsync(CancellationToken.None)).ShouldBe(0);

        _messages.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_reminder_comes_only_once()
    {
        Balance(12);

        await Service().RemindPayoutAfterExitAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromDays(3));

        (await Service().RemindPayoutAfterExitAsync(CancellationToken.None)).ShouldBe(0);
        _messages.Of<SendNotification>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Without_anybody_responsible_nothing_is_lost_the_reminder_follows_once_somebody_is()
    {
        Balance(12);
        _responsibilities.PayoutHandlers.Clear();

        (await Service().RemindPayoutAfterExitAsync(CancellationToken.None)).ShouldBe(0);
        _store.Checks.ShouldBeEmpty(); // not marked, so the next round tries again

        _responsibilities.PayoutHandlers[Erika] = [Hanna];
        (await Service().RemindPayoutAfterExitAsync(CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public async Task Only_the_balance_up_to_the_exit_date_counts()
    {
        Balance(10, "2026-10-01");
        Balance(-10, "2026-10-25"); // a later booking (after the exit date) does not hide what was there at the exit

        (await Service().RemindPayoutAfterExitAsync(CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public async Task Several_people_are_handled_in_one_round()
    {
        var other = Guid.Parse("00000000-0000-0000-0000-0000000000e2");
        _people.People[other] = new PersonInfo(other, "Max Muster", Store, new DateOnly(2026, 10, 15));
        _responsibilities.PayoutHandlers[other] = [Hanna];
        _store.Exited.Add(new ExitedAccount(other, new DateOnly(2026, 10, 15)));
        Balance(12);
        _store.Bookings.Add(new TimeBooking { Id = Guid.NewGuid(), EmployeeId = other, Kind = BookingKind.Correction, Hours = 3, Date = D("2026-10-01"), CreatedBy = Hanna, CreatedAt = _time.GetUtcNow() });

        (await Service().RemindPayoutAfterExitAsync(CancellationToken.None)).ShouldBe(2);

        _messages.Of<SendNotification>().Select(n => n.NotificationId).Distinct().Count().ShouldBe(2);
    }
}
