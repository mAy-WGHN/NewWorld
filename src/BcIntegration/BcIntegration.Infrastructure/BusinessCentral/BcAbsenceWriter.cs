using BcIntegration.Contracts;
using BcIntegration.Core.MasterData;
using BcIntegration.Core.Workforce;
using Microsoft.Extensions.Options;
using Workforce.Contracts;

namespace BcIntegration.Infrastructure.BusinessCentral;

/// <summary>
/// Upsert of an absence via its external ID (the internal AbsenceId): look up, then PATCH with ETag or POST.
/// The BC employee is addressed by its personnel number from the last known state; an unknown employee is a
/// business failure without BC call.
/// </summary>
public sealed class BcAbsenceWriter(BcClient client, IMasterDataStore masterData, IOptions<BcClientOptions> options) : IBcAbsenceWriter
{
    public async Task<BcWriteResult> UpsertAsync(SyncAbsenceToBc absence, CancellationToken cancellationToken)
    {
        if (await masterData.GetEmployeeAsync(absence.EmployeeId, cancellationToken) is not { } employee)
        {
            return new BcWriteResult.Failed(BcSyncErrorCode.ReferencedRecordMissing);
        }

        var body = new BcAbsenceWriteDto(
            absence.AbsenceId,
            employee.PersonnelNumber,
            absence.From,
            absence.To,
            Code(options.Value.AbsenceTypeCodes, absence.Type),
            Code(options.Value.AbsenceStatusCodes, absence.Status),
            absence.Version);

        var existing = await client.FindAbsenceAsync(absence.AbsenceId, cancellationToken);
        var (written, rejected) = await client.WriteAbsenceAsync(existing, body, cancellationToken);
        return written is not null
            ? new BcWriteResult.Succeeded(written.Id)
            : new BcWriteResult.Failed(rejected?.BusinessCode ?? BcSyncErrorCode.Unknown);
    }

    private static string Code<TEnum>(Dictionary<string, string> codes, TEnum value) where TEnum : struct, Enum =>
        codes.TryGetValue(value.ToString(), out var code) ? code : value.ToString();
}
