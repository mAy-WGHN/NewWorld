namespace Portal.Core.Reports;

public enum ReportStatus
{
    Open = 1,
    Done = 2,
}

/// <summary>Who filed the report. Public reports are always <see cref="Employee"/>; the source cannot change afterwards.</summary>
public enum ReportSource
{
    Employee = 1,
    System = 2,
}

public enum ReportCategory
{
    Virus = 1,
    Bug = 2,
    Theft = 3,
    Other = 4,
    Suggestion = 5,
}

public enum ReportSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}
