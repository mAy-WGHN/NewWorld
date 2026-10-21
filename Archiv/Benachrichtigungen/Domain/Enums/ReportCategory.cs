namespace Benachrichtigungen.Domain.Enums;

/// <summary>In der DB und der API als Kleinbuchstaben-String (virus, bug, ...).</summary>
public enum ReportCategory
{
    Virus,
    Bug,
    Theft,
    Other,
    Suggestion
}
