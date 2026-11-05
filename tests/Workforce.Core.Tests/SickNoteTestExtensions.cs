using Workforce.Core.Absences;

namespace Workforce.Core.Tests;

/// <summary>
/// Most tests only need "somebody is sick in this period". New sick notes need a last day and a kind (a first certificate here); an open end
/// only exists for sick notes reported before the end became mandatory, which these helpers recreate for the tests of that old state.
/// </summary>
internal static class SickNoteTestExtensions
{
    public static async Task<AbsenceResult> ReportSickAsync(this AbsenceService service, Guid employeeId, DateOnly from, DateOnly? to, CancellationToken cancellationToken)
    {
        var result = await service.ReportSickAsync(employeeId, from, to ?? from, SickNoteKind.First, electronicCertificate: false, cancellationToken);
        if (to is null && result.Absence is { } absence)
        {
            absence.To = null; // an "old" sick note without a last day
        }

        return result;
    }
}
