using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Workforce.Core.Absences;
using Workforce.Core.Employees;
using Workforce.Infrastructure;

namespace Workforce.Api;

/// <summary>
/// Finding people and the own profile, from the copies of BC data in Workforce (organisation data, class 1): the search for the employee card, the
/// active people of a store and "who am I". The reach is checked per person, like everywhere else; nothing here reveals absences or private data.
/// </summary>
public static class PeopleEndpoints
{
    private const int MaxTake = 50;
    private const int MaxWords = 5;

    public static WebApplication MapPeopleEndpoints(this WebApplication app)
    {
        // By name or personnel number (every word has to match, no case, at least two characters in all) and/or one store. Only people for whom the caller holds
        // employee:read; without the privilege nothing is offered at all.
        app.MapGet("/api/employees", async (
                string? query, Guid? storeId, int? take, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, CancellationToken ct) =>
            {
                if (!await Endpoints.Allows(auth, user, new object(), EmployeePrivileges.Read))
                {
                    return Results.Forbid();
                }

                var words = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaxWords).ToList();
                if (words.Sum(w => w.Length) < 2 && storeId is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "QueryTooShort");
                }

                var people = db.People.AsNoTracking().AsQueryable();
                if (storeId is { } store)
                {
                    people = people.Where(p => p.HomeStoreId == store);
                }

                foreach (var word in words)
                {
                    var pattern = "%" + word.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                    people = people.Where(p => EF.Functions.ILike(p.Name, pattern, "\\") || (p.PersonnelNumber != null && EF.Functions.ILike(p.PersonnelNumber, pattern, "\\")));
                }

                var limit = Math.Clamp(take ?? 20, 1, MaxTake);
                var result = new List<EmployeeSummaryDto>();
                foreach (var person in await people.OrderBy(p => p.Name).ThenBy(p => p.PersonnelNumber).Take(MaxTake * 4).ToListAsync(ct))
                {
                    if (await Endpoints.Allows(auth, user, new EmployeeResource(person.EmployeeId), EmployeePrivileges.Read))
                    {
                        result.Add(new EmployeeSummaryDto(person.EmployeeId, person.PersonnelNumber, person.Name, person.HomeStoreId, person.ExitDate));
                    }

                    if (result.Count >= limit)
                    {
                        break;
                    }
                }

                return Results.Ok(result);
            })
            .RequireAuthorization();

        // The active people of a store (not yet left), for the team view: the same privileges as for the absences of the store.
        app.MapGet("/api/stores/{storeId:guid}/people", async (
                Guid storeId, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, AbsenceService absences, CancellationToken ct) =>
            {
                if (!await Endpoints.Allows(auth, user, new StoreResource(storeId), AbsencePrivileges.ReadAvailability)
                    && !await Endpoints.Allows(auth, user, new StoreResource(storeId), AbsencePrivileges.ReadReason))
                {
                    return Results.Forbid();
                }

                var today = absences.TodayLocal;
                return Results.Ok(await db.People.AsNoTracking()
                    .Where(p => p.HomeStoreId == storeId && (p.ExitDate == null || p.ExitDate >= today))
                    .OrderBy(p => p.Name).Select(p => new StorePersonDto(p.EmployeeId, p.Name)).ToListAsync(ct));
            })
            .RequireAuthorization();

        // Who is signed in: name, number and main store. Everybody sees only themself; no privilege needed, nothing sensitive.
        app.MapGet("/api/me/profile", async (ClaimsPrincipal user, WorkforceDbContext db, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } me)
                {
                    return Results.Forbid();
                }

                if (await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.EmployeeId == me, ct) is not { } person)
                {
                    return Results.NotFound();
                }

                var store = person.HomeStoreId is { } id ? await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == id, ct) : null;
                return Results.Ok(new ProfileDto(person.EmployeeId, person.Name, person.PersonnelNumber, store is null ? null : new ProfileStoreDto(store.StoreId, store.StoreNumber, store.Name)));
            })
            .RequireAuthorization();

        return app;
    }
}
