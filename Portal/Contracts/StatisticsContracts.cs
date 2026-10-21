using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts;

public class StatisticsFilterRequest
{
    /// <summary>YYYY-MM, inklusiv ab dem 1. dieses Monats.</summary>
    [RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$", ErrorMessage = "Format muss YYYY-MM sein.")]
    public string? From { get; init; }

    /// <summary>YYYY-MM, inklusiv bis Monatsende (intern: kleiner als der 1. des Folgemonats).</summary>
    [RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$", ErrorMessage = "Format muss YYYY-MM sein.")]
    public string? To { get; init; }
}

public record MonthlyStatistic(string Month, int Employee, int System, int Total);

public record MonthlyCategoryStatistic(string Month, int Bug, int Virus, int Theft, int Other, int Suggestion, int Total);

public record LocationSeverityStatistic(
    Guid LocationId, string LocationName, int Low, int Medium, int High, int Critical, int Total);

public record PersonnelNumberStatistic(int PersonnelNumber, int Low, int Medium, int High, int Critical, int Total);
