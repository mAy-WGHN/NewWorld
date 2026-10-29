using System.Globalization;
using Portal.Api.Http;
using Portal.Core.Reports;

namespace Portal.Api.Reports;

public sealed record LocationDto(Guid Id, string Name, string StoreCode, bool Archived);

public sealed record ReportDto(
    Guid Id,
    int PersonnelNumber,
    ReportStatus Status,
    ReportSource Source,
    ReportCategory Category,
    string Description,
    ReportSeverity Severity,
    IReadOnlyList<LocationDto> Locations,
    string? Device,
    DateTime? NoticedAt,
    string? NoticedHow,
    string? PriorActivity,
    string? Comment,
    bool Archived,
    Guid? ParentReportId,
    IReadOnlyList<Guid> ChildReportIds,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record PageMeta(int Total, int Offset, int? Limit);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, PageMeta Meta);

/// <summary>The public form. A <c>source</c> sent by the client is not even read: public reports are always from an employee.</summary>
public sealed record CreateReportRequest(
    int? PersonnelNumber,
    ReportCategory? Category,
    ReportSeverity? Severity,
    string? Description,
    List<Guid>? LocationIds,
    string? Device,
    DateTime? NoticedAt,
    string? NoticedHow,
    string? PriorActivity);

/// <summary>PATCH semantics: missing or <c>null</c> = unchanged. The source cannot be changed.</summary>
public sealed record UpdateReportRequest(
    int? PersonnelNumber,
    ReportStatus? Status,
    ReportCategory? Category,
    string? Description,
    ReportSeverity? Severity,
    List<Guid>? LocationIds,
    string? Device,
    DateTime? NoticedAt,
    string? NoticedHow,
    string? PriorActivity,
    string? Comment,
    bool? Archived,
    Guid? ParentReportId);

/// <summary>The filters of the list, read by hand from the query so that every error names its field.</summary>
public sealed class ReportFilter
{
    public const int MaxLimit = 200;

    public static readonly string[] SortFields =
    [
        "id", "personnelNumber", "status", "source", "category", "description", "severity", "device",
        "noticedAt", "noticedHow", "priorActivity", "comment", "archived", "createdAt", "updatedAt", "priority",
    ];

    public ReportStatus? Status { get; private init; }
    public ReportCategory? Category { get; private init; }
    public ReportSeverity? Severity { get; private init; }
    public ReportSource? Source { get; private init; }

    /// <summary>Without it active and archived reports are listed.</summary>
    public bool? Archived { get; private init; }

    public string? Search { get; private init; }
    public int? PersonnelNumber { get; private init; }
    public Guid? LocationId { get; private init; }

    /// <summary>Takes precedence over <see cref="Month"/>.</summary>
    public DateTime? Day { get; private init; }

    public DateTime? MonthStart { get; private init; }
    public string SortBy { get; private init; } = "priority";
    public bool Descending { get; private init; } = true;
    public int Offset { get; private init; }
    public int Limit { get; private init; } = 20;

    public static ReportFilter Parse(IQueryCollection query)
    {
        var errors = new Validator();
        var filter = new ReportFilter
        {
            Status = Enum<ReportStatus>(query, "status", errors),
            Category = Enum<ReportCategory>(query, "category", errors),
            Severity = Enum<ReportSeverity>(query, "severity", errors),
            Source = Enum<ReportSource>(query, "source", errors),
            Archived = Bool(query, "archived", errors),
            Search = query["search"].ToString().Trim().ToLowerInvariant() is { Length: > 0 } search ? search : null,
            PersonnelNumber = Int(query, "personnelNumber", 1, int.MaxValue, "Dieser Wert muss positiv sein.", errors),
            LocationId = Guid.TryParse(query["locationId"], out var location) ? location : null,
            Day = DateTime.TryParse(query["day"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var day) ? day.Date : null,
            MonthStart = Month(query, "month", errors),
            SortBy = ParseSortBy(query, errors),
            Descending = ParseDescending(query, errors),
            Offset = Int(query, "offset", 0, int.MaxValue, "Muss mindestens 0 sein.", errors) ?? 0,
            Limit = Int(query, "limit", 1, MaxLimit, "Muss zwischen 1 und 200 liegen.", errors) ?? 20,
        };
        errors.ThrowIfInvalid();
        return filter;
    }

    /// <summary>"YYYY-MM" as the first day of that month (UTC), <c>null</c> if the value is missing; invalid values are an error.</summary>
    public static DateTime? Month(IQueryCollection query, string name, Validator errors)
    {
        var value = query[name].ToString();
        if (value.Length == 0)
        {
            return null;
        }

        if (DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        {
            return DateTime.SpecifyKind(month, DateTimeKind.Utc);
        }

        errors.Add(name, "Format muss YYYY-MM sein.");
        return null;
    }

    private static T? Enum<T>(IQueryCollection query, string name, Validator errors)
        where T : struct, Enum
    {
        var value = query[name].ToString();
        if (value.Length == 0)
        {
            return null;
        }

        if (System.Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && System.Enum.IsDefined(parsed) && !int.TryParse(value, out _))
        {
            return parsed;
        }

        errors.Add(name, "Ungültiger Wert.");
        return null;
    }

    private static bool? Bool(IQueryCollection query, string name, Validator errors)
    {
        var value = query[name].ToString();
        if (value.Length == 0)
        {
            return null;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        errors.Add(name, "Ungültiger Wert.");
        return null;
    }

    private static int? Int(IQueryCollection query, string name, int min, int max, string message, Validator errors)
    {
        var value = query[name].ToString();
        if (value.Length == 0)
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= min && parsed <= max)
        {
            return parsed;
        }

        errors.Add(name, message);
        return null;
    }

    private static string ParseSortBy(IQueryCollection query, Validator errors)
    {
        var value = query["sortBy"].ToString();
        if (value.Length == 0)
        {
            return "priority";
        }

        if (SortFields.Contains(value))
        {
            return value;
        }

        errors.Add("sortBy", "Ungültiges Sortierfeld.");
        return "priority";
    }

    private static bool ParseDescending(IQueryCollection query, Validator errors)
    {
        var value = query["sortOrder"].ToString();
        if (value.Length == 0)
        {
            return true;
        }

        if (value is "asc" or "desc")
        {
            return value == "desc";
        }

        errors.Add("sortOrder", "Erlaubt sind asc oder desc.");
        return true;
    }
}
