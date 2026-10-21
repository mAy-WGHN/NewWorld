using HrTool_API.Services;

namespace HrTool_API.Tests.Unit;

/// <summary>Die Werte stammen aus einem fertigen BC-Dienstplan (Export „Dienstplan“): Zellen wie <c>0530-1430</c> und die Summen „Gesamt Geplant“ / „Gesamtstunden“.</summary>
public class ScheduleCalculatorTests
{
    private static TimeOnly T(int h, int m = 0) => new(h, m);

    [Theory]
    [InlineData(9, 0, 17, 0, 7.5)]    // 9-17: 8 h − 30 min
    [InlineData(5, 30, 14, 30, 8.5)]  // 0530-1430: 9 h − 30 min
    [InlineData(4, 0, 9, 0, 5.0)]     // 4-9: bis 6 h keine Pause
    [InlineData(4, 0, 10, 0, 6.0)]    // genau 6 h: keine Pause
    [InlineData(14, 0, 22, 0, 7.5)]
    [InlineData(16, 0, 0, 0, 7.5)]    // 16-0: über Mitternacht, 8 h
    [InlineData(20, 0, 0, 0, 4.0)]    // 20-0: 4 h, keine Pause
    public void NetHours_SubtractTheBreak_AndHandleMidnight(int sh, int sm, int eh, int em, double expected)
    {
        var hours = ScheduleCalculator.NetHours(T(sh, sm), T(eh, em));

        Assert.Equal((decimal)expected, hours);
    }

    [Fact]
    public void AShiftOverNineHours_GetsTheLegalMinimumBreakOf45Minutes()
    {
        Assert.Equal(9.25m, ScheduleCalculator.NetHours(T(8), T(18)));
    }

    [Fact]
    public void WithoutTimes_NothingCounts_AndEqualTimesAreZero()
    {
        Assert.Equal(0m, ScheduleCalculator.CountedHours(null, null));
        Assert.Equal(0m, ScheduleCalculator.CountedHours(T(9), null));
        Assert.Equal(0m, ScheduleCalculator.NetHours(T(9), T(9)));
    }

    [Theory]
    [InlineData(33, 30, 141.43)]   // Dienstplan „September“: Soll 141,428… bei 33 h/Woche
    [InlineData(30, 31, 132.86)]   // Oktober (31 Tage): Soll 132,857…
    [InlineData(4, 31, 17.71)]     // MINI 4H: 17,714…
    [InlineData(43, 31, 190.43)]
    public void TargetHours_AreWeeklyHoursTimesDaysDividedBySeven(double weekly, int days, double expected)
    {
        Assert.Equal((decimal)expected, ScheduleCalculator.TargetHours((decimal)weekly, days));
    }

    [Fact]
    public void PlannedPercent_IsPlannedOverTarget_AndNullWithoutTarget()
    {
        Assert.Equal(109.42m, ScheduleCalculator.PlannedPercent(154.75m, 141.43m));   // Zeile aus dem Export
        Assert.Null(ScheduleCalculator.PlannedPercent(10m, null));
        Assert.Null(ScheduleCalculator.PlannedPercent(10m, 0m));
    }
}
