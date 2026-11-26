using Portal.Core.CashRegisters;
using Portal.Core.Logbook;

namespace Portal.Core.Tests;

public class CashRegisterReminderTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly CashRegisterReminderSettings Evening = new(true, new TimeOnly(18, 0));

    private static DateOnly? Due(DateTimeOffset utc, DateOnly? last = null, CashRegisterReminderSettings? settings = null) =>
        CashRegisterReminderPlan.DueDay(settings ?? Evening, utc, Berlin, last);

    [Fact]
    public void Nothing_is_due_before_the_time_and_the_day_is_due_from_the_time_on()
    {
        Due(new(2026, 11, 26, 16, 59, 0, TimeSpan.Zero)).ShouldBeNull(); // 17:59 local (CET)
        Due(new(2026, 11, 26, 17, 0, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 11, 26));
        Due(new(2026, 11, 26, 22, 30, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 11, 26));
    }

    [Fact]
    public void A_day_is_reminded_once_and_the_next_day_again()
    {
        var evening = new DateTimeOffset(2026, 11, 26, 17, 5, 0, TimeSpan.Zero);
        Due(evening, last: new DateOnly(2026, 11, 26)).ShouldBeNull();
        Due(evening, last: new DateOnly(2026, 11, 25)).ShouldBe(new DateOnly(2026, 11, 26));
        Due(evening, last: null).ShouldBe(new DateOnly(2026, 11, 26));
    }

    [Fact]
    public void Switched_off_is_never_due() => Due(new(2026, 11, 26, 20, 0, 0, TimeSpan.Zero), settings: Evening with { Enabled = false }).ShouldBeNull();

    [Fact]
    public void The_summer_time_change_moves_the_hour_but_not_the_local_time()
    {
        // 2026-03-29: clocks go forward at 02:00 local; 18:00 local is 16:00 UTC afterwards (before: 17:00 UTC)
        Due(new(2026, 3, 29, 15, 59, 0, TimeSpan.Zero)).ShouldBeNull();
        Due(new(2026, 3, 29, 16, 0, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 3, 29));
        // winter: 2026-10-25, 18:00 local is 17:00 UTC afterwards
        Due(new(2026, 10, 25, 16, 59, 0, TimeSpan.Zero)).ShouldBeNull();
        Due(new(2026, 10, 25, 17, 0, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 10, 25));
    }

    [Fact]
    public void The_change_of_the_year_and_the_leap_day_count_in_local_time()
    {
        Due(new(2026, 12, 31, 17, 0, 0, TimeSpan.Zero), last: new DateOnly(2026, 12, 30)).ShouldBe(new DateOnly(2026, 12, 31));
        // 23:30 UTC on 31.12. is already 00:30 on 1.1. local: before the time of that day
        Due(new(2026, 12, 31, 23, 30, 0, TimeSpan.Zero), last: new DateOnly(2026, 12, 31)).ShouldBeNull();
        Due(new(2028, 2, 29, 17, 0, 0, TimeSpan.Zero), last: new DateOnly(2028, 2, 28)).ShouldBe(new DateOnly(2028, 2, 29));
    }

    [Fact]
    public void A_time_after_midnight_local_is_still_a_valid_setting()
    {
        var late = new CashRegisterReminderSettings(true, new TimeOnly(23, 59));
        Due(new(2026, 11, 26, 22, 58, 0, TimeSpan.Zero), settings: late).ShouldBeNull(); // 23:58 local
        Due(new(2026, 11, 26, 22, 59, 0, TimeSpan.Zero), settings: late).ShouldBe(new DateOnly(2026, 11, 26));
    }

    [Fact]
    public void A_district_leader_reaches_all_stores_of_the_own_district_and_nothing_else()
    {
        Guid boss = Guid.NewGuid(), other = Guid.NewGuid(), d1 = Guid.NewGuid(), d2 = Guid.NewGuid(), r1 = Guid.NewGuid();
        Guid s1 = Guid.NewGuid(), s2 = Guid.NewGuid(), s3 = Guid.NewGuid();
        var structure = new LogbookStructure(
            [new LogbookDistrict(d1, "Süd", [boss]), new LogbookDistrict(d2, "Nord", [other])],
            [new LogbookRegion(r1, d1, "Region", [other])],
            [new LogbookStoreAssignment(s1, d1, r1), new LogbookStoreAssignment(s2, d1, null), new LogbookStoreAssignment(s3, d2, null)]);

        CashRegisterReach.DistrictStores(structure, boss).ShouldBe([s1, s2], ignoreOrder: true);
        CashRegisterReach.DistrictStores(structure, other).ShouldBe([s3]); // leads a region of d1, but is district leader only of d2
        CashRegisterReach.DistrictStores(structure, Guid.NewGuid()).ShouldBeEmpty();
    }
}
