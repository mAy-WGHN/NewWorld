using Microsoft.EntityFrameworkCore;
using Portal.Api.Http;
using Portal.Core;
using Portal.Core.Reports;
using Portal.Infrastructure;
using Portal.Infrastructure.Reports;
using Shared.Authorization;

namespace Portal.Api.Reports;

public sealed record ReportRecipientsDto(ReportCategory Category, IReadOnlyList<Guid> EmployeeIds);

public sealed record SetReportRecipientsRequest(List<Guid>? EmployeeIds);

/// <summary>
/// Who is told about new reports of which category (e.g. the auditor for theft, IT for bugs and viruses). Maintained by the administration;
/// nobody set for a category means nobody is told. The people are checked against the copy of the employees.
/// </summary>
public static class ReportRecipientEndpoints
{
    public static WebApplication MapReportRecipientEndpoints(this WebApplication app)
    {
        app.MapGet("/api/report-recipients", async (PortalDbContext db, CancellationToken ct) => Answer.Ok(await AllAsync(db, ct)))
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.ReportManageRecipients));

        app.MapPut("/api/report-recipients/{category}", async (string category, SetReportRecipientsRequest request, PortalDbContext db, CancellationToken ct) =>
            {
                if (!Enum.TryParse<ReportCategory>(category, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(category, out _))
                {
                    throw ApiException.NotFound();
                }

                if (request.EmployeeIds is null)
                {
                    throw ApiException.Validation("employeeIds", Validator.Required);
                }

                var ids = request.EmployeeIds.Distinct().ToList();
                var known = await db.People.Where(p => ids.Contains(p.EmployeeId)).CountAsync(ct);
                if (known != ids.Count)
                {
                    throw ApiException.Validation("employeeIds", "Mindestens eine Person ist unbekannt.");
                }

                await db.ReportRecipients.Where(r => r.Category == parsed).ExecuteDeleteAsync(ct);
                db.ReportRecipients.AddRange(ids.Select(id => new ReportRecipientRecord { Category = parsed, EmployeeId = id }));
                await db.SaveChangesAsync(ct);
                return Answer.Ok((await AllAsync(db, ct)).Single(r => r.Category == parsed));
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.ReportManageRecipients));

        return app;
    }

    private static async Task<List<ReportRecipientsDto>> AllAsync(PortalDbContext db, CancellationToken ct)
    {
        var rows = (await db.ReportRecipients.AsNoTracking().ToListAsync(ct)).ToLookup(r => r.Category, r => r.EmployeeId);
        return [.. Enum.GetValues<ReportCategory>().Select(c => new ReportRecipientsDto(c, [.. rows[c].Order()]))];
    }
}
