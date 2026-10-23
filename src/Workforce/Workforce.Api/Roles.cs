namespace Workforce.Api;

/// <summary>App roles of calling services.</summary>
public static class WorkforceRoles
{
    /// <summary>Reads the details of an absence including the type (for the later HR API).</summary>
    public const string AbsenceDetailsRead = "Workforce.AbsenceDetails.Read";

    public static readonly IReadOnlyList<string> All = [AbsenceDetailsRead];
}
