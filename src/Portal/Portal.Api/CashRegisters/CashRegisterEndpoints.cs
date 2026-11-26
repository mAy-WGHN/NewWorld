using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Portal.Api.Http;
using Portal.Api.Reports;
using Portal.Core;
using Portal.Core.CashRegisters;
using Portal.Infrastructure;
using Portal.Infrastructure.CashRegisters;
using Portal.Infrastructure.Logbook;
using Shared.Authorization;

namespace Portal.Api.CashRegisters;

public sealed record CashRegisterDto(
    Guid Id, Guid StoreId, string StoreNumber, string StoreName, string Name, bool Sealed, DateTimeOffset? SealedAt,
    DateTimeOffset LastRecordedAt, string RecordedBy, string? RecordedByName);

public sealed record CashRegisterStoreDto(Guid Id, string Number, string Name);

public sealed record CashRegisterSummaryDto(int Stores, int Registers, int Sealed, int NotSealed);

public sealed record CashRegisterListDto(CashRegisterSummaryDto Summary, IReadOnlyList<CashRegisterStoreDto> Stores, IReadOnlyList<CashRegisterDto> Items);

public sealed record CashRegisterEventDto(Guid Id, Guid StoreId, string StoreNumber, string StoreName, Guid RegisterId, string RegisterName, string Kind, DateTimeOffset At, string By, string? ByName);

public sealed record SaveCashRegisterRequest(Guid StoreId, string? Name);

/// <summary><c>Time</c> is the local time of day as <c>HH:mm</c>.</summary>
public sealed record CashRegisterReminderSettingsDto(bool Enabled, string Time);

public sealed record SaveCashRegisterReminderSettingsRequest(bool? Enabled, string? Time);

/// <summary>
/// The Kassenregister (Filialbetrieb): the cash registers of the stores with their seal. Reading, recording (create, edit, renew the seal) and deleting are
/// three privileges, each scopable by store. Recording or editing a register removes its seal, renewing sets it (TODO(Fachlogik): confirm this meaning).
/// The history of a store keeps every step, deleted registers by name. Organisation data only (class 1); the person is shown by initials and name.
/// </summary>
public static class CashRegisterEndpoints
{
    private const int MaxNameLength = 100;
    private const int MaxHistory = 500;

    public static WebApplication MapCashRegisterEndpoints(this WebApplication app)
    {
        var record = AppPolicies.Privilege(PortalPrivileges.CashRegisterRecord);
        var delete = AppPolicies.Privilege(PortalPrivileges.CashRegisterDelete);

        app.MapGet("/api/cash-registers", async (Guid? storeId, ClaimsPrincipal user, ReportAccess access, LogbookStructureStore structures, PortalDbContext db, CancellationToken ct) =>
            {
                var stores = await StoresInReachAsync(await ReadReachAsync(user, access, structures, ct), db, ct);
                var ids = stores.Select(s => s.StoreId).ToList();
                var registers = await db.CashRegisters.AsNoTracking().Where(r => ids.Contains(r.StoreId)).ToListAsync(ct);
                var summary = new CashRegisterSummaryDto(stores.Count, registers.Count, registers.Count(r => r.SealedAt is not null), registers.Count(r => r.SealedAt is null));
                var shown = storeId is { } only ? registers.Where(r => r.StoreId == only) : registers;
                var names = await NamesAsync(db, shown.Select(r => r.LastRecordedBy), ct);
                var byStore = stores.ToDictionary(s => s.StoreId);
                return Answer.Ok(new CashRegisterListDto(
                    summary,
                    [.. stores.Select(s => new CashRegisterStoreDto(s.StoreId, s.StoreNumber, s.Name))],
                    [.. shown.OrderBy(r => byStore[r.StoreId].StoreNumber, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).Select(r => ToDto(r, byStore[r.StoreId], names))]));
            })
            .RequireAuthorization();

        app.MapPost("/api/cash-registers", async (SaveCashRegisterRequest request, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                var name = ValidName(request.Name);
                var store = await EnsureStoreAsync(request.StoreId, access.ReachOf(user, PortalPrivileges.CashRegisterRecord), db, ct);
                await EnsureUniqueAsync(db, store.StoreId, name, except: null, ct);
                var now = clock.GetUtcNow();
                var me = Me(user);
                var row = new CashRegisterRecord { StoreId = store.StoreId, Name = name, LastRecordedAt = now, LastRecordedBy = me, CreatedAt = now };
                db.CashRegisters.Add(row);
                db.CashRegisterEvents.Add(Event(row, CashRegisterEventKind.Recorded, now, me));
                await SaveAsync(db, ct);
                return Answer.Created(ToDto(row, store, await NamesAsync(db, [me], ct)));
            })
            .RequireAuthorization(record);

        // "Bearbeiten": rename or move; counts as a new recording, so the seal falls away.
        app.MapPut("/api/cash-registers/{id:guid}", async (Guid id, SaveCashRegisterRequest request, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                var name = ValidName(request.Name);
                var row = await FindAsync(db, id, access.ReachOf(user, PortalPrivileges.CashRegisterRecord), "update", ct);
                var store = await EnsureStoreAsync(request.StoreId, access.ReachOf(user, PortalPrivileges.CashRegisterRecord), db, ct);
                await EnsureUniqueAsync(db, store.StoreId, name, except: id, ct);
                var now = clock.GetUtcNow();
                var me = Me(user);
                row.StoreId = store.StoreId;
                row.Name = name;
                row.LastRecordedAt = now;
                row.LastRecordedBy = me;
                row.SealedAt = null;
                row.SealedBy = null;
                db.CashRegisterEvents.Add(Event(row, CashRegisterEventKind.Changed, now, me));
                await SaveAsync(db, ct);
                return Answer.Ok(ToDto(row, store, await NamesAsync(db, [me], ct)));
            })
            .RequireAuthorization(record);

        app.MapPost("/api/cash-registers/{id:guid}/seal", async (Guid id, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                var row = await FindAsync(db, id, access.ReachOf(user, PortalPrivileges.CashRegisterRecord), "update", ct);
                var now = clock.GetUtcNow();
                var me = Me(user);
                row.SealedAt = now;
                row.SealedBy = me;
                db.CashRegisterEvents.Add(Event(row, CashRegisterEventKind.Sealed, now, me));
                await db.SaveChangesAsync(ct);
                var store = await db.Stores.AsNoTracking().SingleAsync(s => s.StoreId == row.StoreId, ct);
                return Answer.Ok(ToDto(row, store, await NamesAsync(db, [row.LastRecordedBy], ct)));
            })
            .RequireAuthorization(record);

        app.MapDelete("/api/cash-registers/{id:guid}", async (Guid id, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                var row = await FindAsync(db, id, access.ReachOf(user, PortalPrivileges.CashRegisterDelete), "delete", ct);
                db.CashRegisterEvents.Add(Event(row, CashRegisterEventKind.Deleted, clock.GetUtcNow(), Me(user)));
                db.CashRegisters.Remove(row);
                await db.SaveChangesAsync(ct);
                return Answer.NoContent();
            })
            .RequireAuthorization(delete);

        // The history of one store or of all stores in reach, newest first.
        app.MapGet("/api/cash-registers/history", async (Guid? storeId, int? take, ClaimsPrincipal user, ReportAccess access, LogbookStructureStore structures, PortalDbContext db, CancellationToken ct) =>
            {
                var scope = await ReadReachAsync(user, access, structures, ct);
                if (storeId is { } one && (scope is null || (!scope.Unrestricted && !scope.StoreIds.Contains(one))))
                {
                    throw new ApiException(StatusCodes.Status403Forbidden, EnvelopeAuthorizationResultHandler.Message("read"));
                }

                var stores = (await StoresInReachAsync(scope, db, ct, includeClosed: true)).ToDictionary(s => s.StoreId);
                var query = db.CashRegisterEvents.AsNoTracking().Where(e => stores.Keys.Contains(e.StoreId));
                if (storeId is { } only)
                {
                    query = query.Where(e => e.StoreId == only);
                }

                var events = await query.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Take(Math.Clamp(take ?? 100, 1, MaxHistory)).ToListAsync(ct);
                var names = await NamesAsync(db, events.Select(e => e.By), ct);
                return Answer.Ok(events.Select(e => new CashRegisterEventDto(
                    e.Id, e.StoreId, stores[e.StoreId].StoreNumber, stores[e.StoreId].Name, e.RegisterId, e.RegisterName, e.Kind.ToString().ToLowerInvariant(),
                    e.At, CashRegisterInitials.Of(names.GetValueOrDefault(e.By)), names.GetValueOrDefault(e.By))));
            })
            .RequireAuthorization();

        // The daily reminder for registers that are not sealed: on or off and the local time of day.
        var manage = AppPolicies.Privilege(PortalPrivileges.CashRegisterManageReminders);
        app.MapGet("/api/cash-registers/reminder-settings", async (CashRegisterReminderService service, CancellationToken ct) =>
                Answer.Ok(ToDto(await service.GetSettingsAsync(ct))))
            .RequireAuthorization(manage);

        app.MapPut("/api/cash-registers/reminder-settings", async (SaveCashRegisterReminderSettingsRequest request, ClaimsPrincipal user, CashRegisterReminderService service, CancellationToken ct) =>
            {
                var validator = new Validator();
                if (request.Enabled is null)
                {
                    validator.Add("enabled", Validator.Required);
                }

                if (!TimeOnly.TryParseExact(request.Time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time))
                {
                    validator.Add("time", "Bitte eine Uhrzeit im Format HH:mm angeben, z. B. 18:00.");
                }

                validator.ThrowIfInvalid();
                return Answer.Ok(ToDto(await service.SaveSettingsAsync(new CashRegisterReminderSettings(request.Enabled!.Value, time), Me(user), ct)));
            })
            .RequireAuthorization(manage);

        return app;
    }

    private static CashRegisterReminderSettingsDto ToDto(CashRegisterReminderSettings s) => new(s.Enabled, s.Time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Who may look and where: the stores of the privilege plus <b>always every store of the districts a person leads</b> (the hierarchy of the Berichtsheft),
    /// even without the privilege. Nobody with neither gets 403.
    /// </summary>
    private static async Task<PrivilegeReach> ReadReachAsync(ClaimsPrincipal user, ReportAccess access, LogbookStructureStore structures, CancellationToken ct)
    {
        var own = access.ReachOf(user, PortalPrivileges.CashRegisterRead);
        if (own is { Unrestricted: true })
        {
            return own;
        }

        var district = CashRegisterReach.DistrictStores(await structures.LoadAsync(ct), Me(user));
        if (own is null && district.Count == 0)
        {
            throw new ApiException(StatusCodes.Status403Forbidden, EnvelopeAuthorizationResultHandler.Message("read"));
        }

        return PrivilegeReach.Stores((own?.StoreIds ?? new HashSet<Guid>()).Concat(district), own?.EmployeeIds ?? new HashSet<Guid>());
    }

    private static Guid Me(ClaimsPrincipal user) => Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var id) ? id : Guid.Empty;

    private static string ValidName(string? name)
    {
        var validator = new Validator();
        validator.RequiredText("name", name, MaxNameLength);
        validator.ThrowIfInvalid();
        return name!.Trim();
    }

    private static CashRegisterEventRecord Event(CashRegisterRecord row, CashRegisterEventKind kind, DateTimeOffset at, Guid by) =>
        new() { StoreId = row.StoreId, RegisterId = row.Id, RegisterName = row.Name, Kind = kind, At = at, By = by };

    private static CashRegisterDto ToDto(CashRegisterRecord r, Portal.Infrastructure.Messaging.StoreRecord store, IReadOnlyDictionary<Guid, string> names) => new(
        r.Id, r.StoreId, store.StoreNumber, store.Name, r.Name, r.SealedAt is not null, r.SealedAt, r.LastRecordedAt,
        CashRegisterInitials.Of(names.GetValueOrDefault(r.LastRecordedBy)), names.GetValueOrDefault(r.LastRecordedBy));

    private static async Task<Dictionary<Guid, string>> NamesAsync(PortalDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        return await db.People.AsNoTracking().Where(p => distinct.Contains(p.EmployeeId)).ToDictionaryAsync(p => p.EmployeeId, p => p.Name, ct);
    }

    /// <summary>The open stores the privilege reaches (without any reach nothing), by store number. History also wants closed ones.</summary>
    private static async Task<List<Portal.Infrastructure.Messaging.StoreRecord>> StoresInReachAsync(PrivilegeReach? reach, PortalDbContext db, CancellationToken ct, bool includeClosed = false)
    {
        if (reach is null)
        {
            return [];
        }

        var query = db.Stores.AsNoTracking().Where(s => includeClosed || s.IsActive);
        if (!reach.Unrestricted)
        {
            var ids = reach.StoreIds.ToArray();
            query = query.Where(s => ids.Contains(s.StoreId));
        }

        return await query.OrderBy(s => s.StoreNumber).ToListAsync(ct);
    }

    /// <summary>The store must be in reach (403), exist (404) and be open (422).</summary>
    private static async Task<Portal.Infrastructure.Messaging.StoreRecord> EnsureStoreAsync(Guid storeId, PrivilegeReach? reach, PortalDbContext db, CancellationToken ct)
    {
        if (reach is null || (!reach.Unrestricted && !reach.StoreIds.Contains(storeId)))
        {
            throw new ApiException(StatusCodes.Status403Forbidden, EnvelopeAuthorizationResultHandler.Message("update"));
        }

        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == storeId, ct) ?? throw ApiException.NotFound();
        return store.IsActive ? store : throw ApiException.Validation("storeId", "Diese Filiale ist geschlossen.");
    }

    /// <summary>The register, if its store is in reach; otherwise 403 (exists) or 404.</summary>
    private static async Task<CashRegisterRecord> FindAsync(PortalDbContext db, Guid id, PrivilegeReach? reach, string verb, CancellationToken ct)
    {
        var row = await db.CashRegisters.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw ApiException.NotFound();
        return reach is not null && (reach.Unrestricted || reach.StoreIds.Contains(row.StoreId))
            ? row
            : throw new ApiException(StatusCodes.Status403Forbidden, EnvelopeAuthorizationResultHandler.Message(verb));
    }

    private static async Task EnsureUniqueAsync(PortalDbContext db, Guid storeId, string name, Guid? except, CancellationToken ct)
    {
        // a few registers per store: compared in memory, case-insensitive
        var names = await db.CashRegisters.AsNoTracking().Where(r => r.StoreId == storeId && r.Id != except).Select(r => r.Name).ToListAsync(ct);
        if (names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw ApiException.Conflict("In dieser Filiale gibt es schon eine Kasse mit diesem Namen.");
        }
    }

    private static async Task SaveAsync(PortalDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw ApiException.Conflict("In dieser Filiale gibt es schon eine Kasse mit diesem Namen.");
        }
    }
}
