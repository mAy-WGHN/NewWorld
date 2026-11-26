using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Portal.Api.Http;
using Portal.Api.Reports;
using Portal.Core;
using Portal.Core.CashRegisters;
using Portal.Core.Faqs;
using Portal.Infrastructure;
using Portal.Infrastructure.CashRegisters;
using Portal.Infrastructure.Logbook;
using Shared.Authorization;

namespace Portal.Api.CashRegisters;

/// <summary>
/// A cash register. <c>SealedBy</c> is the initials of the person who last recorded it; <c>SealAttachedBy</c> (initials) and <c>SealPhotoId</c> belong to the
/// current seal (from its form), <c>StateChangedAt</c> is when the state was set (taken by the service).
/// </summary>
public sealed record CashRegisterDto(
    Guid Id, Guid StoreId, string StoreNumber, string StoreName, string Name, bool Sealed, DateTimeOffset StateChangedAt,
    DateTimeOffset LastRecordedAt, string RecordedBy, string? RecordedByName, Guid? SealPhotoId, string? SealAttachedBy, string? SealAttachedByName);

public sealed record CashRegisterStoreDto(Guid Id, string Number, string Name);

public sealed record CashRegisterSummaryDto(int Stores, int Registers, int Sealed, int NotSealed);

public sealed record CashRegisterListDto(CashRegisterSummaryDto Summary, IReadOnlyList<CashRegisterStoreDto> Stores, IReadOnlyList<CashRegisterDto> Items);

public sealed record CashRegisterEventDto(
    Guid Id, Guid StoreId, string StoreNumber, string StoreName, Guid RegisterId, string RegisterName, string Kind, DateTimeOffset At, string By, string? ByName,
    Guid? PhotoId, string? AttachedBy, string? AttachedByName);

/// <summary>
/// The form of a seal: the photo (uploaded first, its id) and who attached the seal (the id of an employee). The date and time are taken by the service.
/// </summary>
public sealed record SealFormRequest(Guid? PhotoId, Guid? SealedBy);

/// <summary>
/// <c>Sealed</c>: the seal state to set; left out when editing it stays as it is, left out when creating the register starts not sealed. Setting a seal (new
/// state sealed) needs the form: <c>PhotoId</c> and <c>SealedBy</c>.
/// </summary>
public sealed record SaveCashRegisterRequest(Guid StoreId, string? Name, bool? Sealed = null, Guid? PhotoId = null, Guid? SealedBy = null);

public sealed record CashRegisterPhotoDto(Guid Id);

public sealed record CashRegisterPersonDto(Guid Id, string Name);

/// <summary><c>Time</c> is the local time of day as <c>HH:mm</c>.</summary>
public sealed record CashRegisterReminderSettingsDto(bool Enabled, string Time);

public sealed record SaveCashRegisterReminderSettingsRequest(bool? Enabled, string? Time);

/// <summary>
/// The Kassenregister (Filialbetrieb): the cash registers of the stores with their seal. Reading, recording (create, edit, seal) and deleting are three
/// privileges, each scopable by store. The seal is a plain state that is set explicitly. <b>Setting a seal is a form</b>: a photo of the seal and who
/// attached it are required, the date and time are taken by the service; lifting it ("Siegel aufheben", or editing with sealed=false) needs nothing; a new
/// register starts not sealed. The history of a store keeps every step (with the photo and the person of a seal), deleted registers by name.
/// Organisation data (class 1); the person is shown by initials and name. TODO(Entscheidung): how long the photos are kept (they can show people).
/// </summary>
public static class CashRegisterEndpoints
{
    private const int MaxNameLength = 100;
    private const int MaxHistory = 500;
    private const int MaxPeople = 20;

    public static WebApplication MapCashRegisterEndpoints(this WebApplication app)
    {
        var record = AppPolicies.Privilege(PortalPrivileges.CashRegisterRecord);
        var delete = AppPolicies.Privilege(PortalPrivileges.CashRegisterDelete);

        app.MapGet("/api/cash-registers", async (Guid? storeId, ClaimsPrincipal user, ReportAccess access, LogbookStructureStore structures, PortalDbContext db, CancellationToken ct) =>
            {
                var stores = await StoresInReachAsync(await ReadReachAsync(user, access, structures, ct), db, ct);
                var ids = stores.Select(s => s.StoreId).ToList();
                var registers = await db.CashRegisters.AsNoTracking().Where(r => ids.Contains(r.StoreId)).ToListAsync(ct);
                var summary = new CashRegisterSummaryDto(stores.Count, registers.Count, registers.Count(r => r.IsSealed), registers.Count(r => !r.IsSealed));
                var shown = (storeId is { } only ? registers.Where(r => r.StoreId == only) : registers).ToList();
                var names = await NamesAsync(db, shown.SelectMany(r => new[] { r.LastRecordedBy, r.SealAttachedBy ?? Guid.Empty }), ct);
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
                SealForm? form = request.Sealed == true ? await TakeFormAsync(db, request.PhotoId, request.SealedBy, me, now, ct) : null;
                var row = new CashRegisterRecord { StoreId = store.StoreId, Name = name, LastRecordedAt = now, LastRecordedBy = me, CreatedAt = now, StateChangedAt = now, StateChangedBy = me };
                db.CashRegisters.Add(row);
                db.CashRegisterEvents.Add(Event(row, CashRegisterEventKind.Recorded, now, me));
                if (form is { } sealForm)
                {
                    row.SetSealed(true, now, me, sealForm.PhotoId, sealForm.SealedBy);
                    db.CashRegisterEvents.Add(Event(row, CashRegisterEventKind.Sealed, now.AddMicroseconds(1), me, sealForm));
                }

                await SaveAsync(db, ct);
                return Answer.Created(ToDto(row, store, await NamesAsync(db, [me, row.SealAttachedBy ?? Guid.Empty], ct)));
            })
            .RequireAuthorization(record);

        // "Bearbeiten": rename, move and set the seal state (sealed with the form, or the seal lifted); counts as a new recording.
        app.MapPut("/api/cash-registers/{id:guid}", async (Guid id, SaveCashRegisterRequest request, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                var name = ValidName(request.Name);
                var row = await FindAsync(db, id, access.ReachOf(user, PortalPrivileges.CashRegisterRecord), "update", ct);
                var store = await EnsureStoreAsync(request.StoreId, access.ReachOf(user, PortalPrivileges.CashRegisterRecord), db, ct);
                await EnsureUniqueAsync(db, store.StoreId, name, except: id, ct);
                var now = clock.GetUtcNow();
                var me = Me(user);
                var wasSealed = row.IsSealed;
                var becomesSealed = request.Sealed == true && !wasSealed;
                SealForm? form = becomesSealed ? await TakeFormAsync(db, request.PhotoId, request.SealedBy, me, now, ct) : null;
                var changed = row.StoreId != store.StoreId || row.Name != name;
                row.StoreId = store.StoreId;
                row.Name = name;
                row.LastRecordedAt = now;
                row.LastRecordedBy = me;
                var step = 0;
                if (form is { } sealForm)
                {
                    row.SetSealed(true, now, me, sealForm.PhotoId, sealForm.SealedBy);
                }
                else if (request.Sealed == false && wasSealed)
                {
                    row.SetSealed(false, now, me);
                }

                // every step is its own line of the history, in the order they happened (a microsecond apart, the precision of the database, so the order is kept)
                if (changed || row.IsSealed == wasSealed)
                {
                    db.CashRegisterEvents.Add(Event(row, CashRegisterEventKind.Changed, now.AddMicroseconds(step++), me));
                }

                if (row.IsSealed != wasSealed)
                {
                    db.CashRegisterEvents.Add(row.IsSealed
                        ? Event(row, CashRegisterEventKind.Sealed, now.AddMicroseconds(step), me, form)
                        : Event(row, CashRegisterEventKind.Unsealed, now.AddMicroseconds(step), me));
                }

                await SaveAsync(db, ct);
                return Answer.Ok(ToDto(row, store, await NamesAsync(db, [me, row.SealAttachedBy ?? Guid.Empty], ct)));
            })
            .RequireAuthorization(record);

        // "Siegel anbringen / erneuern": the form of a seal (photo and who attached it); the date and time are taken here.
        app.MapPost("/api/cash-registers/{id:guid}/seal", async (Guid id, SealFormRequest request, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                var row = await FindAsync(db, id, access.ReachOf(user, PortalPrivileges.CashRegisterRecord), "update", ct);
                var now = clock.GetUtcNow();
                var me = Me(user);
                var form = await TakeFormAsync(db, request.PhotoId, request.SealedBy, me, now, ct);
                row.SetSealed(true, now, me, form.PhotoId, form.SealedBy); // also when it is sealed already: a renewed seal is a step of its own
                db.CashRegisterEvents.Add(Event(row, CashRegisterEventKind.Sealed, now, me, form));
                await db.SaveChangesAsync(ct);
                var store = await db.Stores.AsNoTracking().SingleAsync(s => s.StoreId == row.StoreId, ct);
                return Answer.Ok(ToDto(row, store, await NamesAsync(db, [row.LastRecordedBy, row.SealAttachedBy ?? Guid.Empty], ct)));
            })
            .RequireAuthorization(record);

        // "Siegel aufheben": the drawer is not sealed any more. Without a seal there is nothing to do (no history line).
        app.MapPost("/api/cash-registers/{id:guid}/unseal", async (Guid id, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                var row = await FindAsync(db, id, access.ReachOf(user, PortalPrivileges.CashRegisterRecord), "update", ct);
                if (row.IsSealed)
                {
                    var now = clock.GetUtcNow();
                    row.SetSealed(false, now, Me(user));
                    db.CashRegisterEvents.Add(Event(row, CashRegisterEventKind.Unsealed, now, Me(user)));
                    await db.SaveChangesAsync(ct);
                }

                var store = await db.Stores.AsNoTracking().SingleAsync(s => s.StoreId == row.StoreId, ct);
                return Answer.Ok(ToDto(row, store, await NamesAsync(db, [row.LastRecordedBy, row.SealAttachedBy ?? Guid.Empty], ct)));
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
                if (storeId is { } one && (!scope.Unrestricted && !scope.StoreIds.Contains(one)))
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
                var names = await NamesAsync(db, events.SelectMany(e => new[] { e.By, e.AttachedBy ?? Guid.Empty }), ct);
                return Answer.Ok(events.Select(e => new CashRegisterEventDto(
                    e.Id, e.StoreId, stores[e.StoreId].StoreNumber, stores[e.StoreId].Name, e.RegisterId, e.RegisterName, e.Kind.ToString().ToLowerInvariant(),
                    e.At, CashRegisterInitials.Of(names.GetValueOrDefault(e.By)), names.GetValueOrDefault(e.By),
                    e.PhotoId, e.AttachedBy is { } by ? CashRegisterInitials.Of(names.GetValueOrDefault(by)) : null, e.AttachedBy is { } by2 ? names.GetValueOrDefault(by2) : null)));
            })
            .RequireAuthorization();

        MapPhotos(app, record);

        // The people to choose from in the form of a seal ("who attached the seal"): name search in the copy of the employees.
        app.MapGet("/api/cash-registers/people", async (string? query, PortalDbContext db, CancellationToken ct) =>
            {
                var text = (query ?? string.Empty).Trim();
                if (text.Length < 2)
                {
                    return Answer.Ok(Array.Empty<CashRegisterPersonDto>());
                }

                var pattern = "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
                var people = await db.People.AsNoTracking().Where(p => EF.Functions.ILike(p.Name, pattern, "\\")).OrderBy(p => p.Name).Take(MaxPeople).ToListAsync(ct);
                return Answer.Ok(people.Select(p => new CashRegisterPersonDto(p.EmployeeId, p.Name)));
            })
            .RequireAuthorization(record);

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

    /// <summary>
    /// The photo of a seal is uploaded first (a person who may record registers), then named in the form. Looking at it follows the reach of the register
    /// it belongs to (like the history); until a seal uses it only the person who uploaded it can see it.
    /// </summary>
    private static void MapPhotos(WebApplication app, string record)
    {
        app.MapPost("/api/cash-registers/photos", async (HttpRequest request, ClaimsPrincipal user, PortalDbContext db, ICashRegisterPhotoStorage storage, TimeProvider clock, CancellationToken ct) =>
            {
                if (!request.HasFormContentType || (await request.ReadFormAsync(ct)).Files.GetFile("file") is not { } file)
                {
                    throw ApiException.BadRequest("Es wurde keine Datei übermittelt.");
                }

                if (file.Length > FaqImageFormat.MaxBytes)
                {
                    throw ApiException.Validation("file", "Die Datei ist zu groß. Erlaubt sind höchstens 5 MB.");
                }

                await using var stream = file.OpenReadStream();
                var header = new byte[FaqImageFormat.HeaderLength];
                var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
                var extension = FaqImageFormat.DetectExtension(header.AsSpan(0, read))
                    ?? throw ApiException.Validation("file", "Nur PNG-, JPEG- oder WebP-Bilder sind erlaubt.");
                stream.Position = 0;

                var photo = new CashRegisterPhotoRecord { Extension = extension, UploadedBy = Me(user), UploadedAt = clock.GetUtcNow() };
                await storage.SaveAsync(photo.Id, extension, stream, ct);
                db.CashRegisterPhotos.Add(photo);
                await db.SaveChangesAsync(ct);
                return Answer.Created(new CashRegisterPhotoDto(photo.Id));
            })
            .RequireAuthorization(record);

        app.MapGet("/api/cash-registers/photos/{id:guid}", async (Guid id, ClaimsPrincipal user, ReportAccess access, LogbookStructureStore structures, PortalDbContext db, ICashRegisterPhotoStorage storage, CancellationToken ct) =>
            {
                var photo = await db.CashRegisterPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw ApiException.NotFound();
                var storeId = await db.CashRegisterEvents.AsNoTracking().Where(e => e.PhotoId == id).Select(e => (Guid?)e.StoreId).FirstOrDefaultAsync(ct);
                if (storeId is { } store)
                {
                    var reach = await ReadReachAsync(user, access, structures, ct);
                    if (!reach.Unrestricted && !reach.StoreIds.Contains(store))
                    {
                        throw new ApiException(StatusCodes.Status403Forbidden, EnvelopeAuthorizationResultHandler.Message("read"));
                    }
                }
                else if (photo.UploadedBy != Me(user))
                {
                    throw ApiException.NotFound(); // not used yet: private to who uploaded it
                }

                var stream = storage.Open(photo.Id, photo.Extension) ?? throw ApiException.NotFound();
                return Results.Stream(stream, FaqImageFormat.ContentType(photo.Extension));
            })
            .RequireAuthorization();
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

    /// <summary>What a seal needs from its form, checked: the photo (uploaded by this person, not used yet) and the person who attached the seal (known).</summary>
    private readonly record struct SealForm(Guid PhotoId, Guid SealedBy);

    private static async Task<SealForm> TakeFormAsync(PortalDbContext db, Guid? photoId, Guid? sealedBy, Guid me, DateTimeOffset now, CancellationToken ct)
    {
        var validator = new Validator();
        if (photoId is null)
        {
            validator.Add("photoId", "Bitte ein Foto des Siegels hochladen.");
        }

        if (sealedBy is null)
        {
            validator.Add("sealedBy", "Bitte angeben, wer das Siegel angebracht hat.");
        }

        validator.ThrowIfInvalid();

        var photo = await db.CashRegisterPhotos.FirstOrDefaultAsync(p => p.Id == photoId!.Value, ct);
        if (photo is null || photo.UploadedBy != me || photo.UsedAt is not null)
        {
            throw ApiException.Validation("photoId", "Das Foto ist unbekannt oder wurde schon verwendet. Bitte noch einmal hochladen.");
        }

        if (!await db.People.AnyAsync(p => p.EmployeeId == sealedBy!.Value, ct))
        {
            throw ApiException.Validation("sealedBy", "Diese Person ist unbekannt.");
        }

        photo.UsedAt = now;
        return new SealForm(photo.Id, sealedBy!.Value);
    }

    private static CashRegisterEventRecord Event(CashRegisterRecord row, CashRegisterEventKind kind, DateTimeOffset at, Guid by, SealForm? form = null) =>
        new() { StoreId = row.StoreId, RegisterId = row.Id, RegisterName = row.Name, Kind = kind, At = at, By = by, PhotoId = form?.PhotoId, AttachedBy = form?.SealedBy };

    private static CashRegisterDto ToDto(CashRegisterRecord r, Portal.Infrastructure.Messaging.StoreRecord store, IReadOnlyDictionary<Guid, string> names) => new(
        r.Id, r.StoreId, store.StoreNumber, store.Name, r.Name, r.IsSealed, r.StateChangedAt, r.LastRecordedAt,
        CashRegisterInitials.Of(names.GetValueOrDefault(r.LastRecordedBy)), names.GetValueOrDefault(r.LastRecordedBy),
        r.SealPhotoId,
        r.SealAttachedBy is { } by ? CashRegisterInitials.Of(names.GetValueOrDefault(by)) : null,
        r.SealAttachedBy is { } by2 ? names.GetValueOrDefault(by2) : null);

    private static async Task<Dictionary<Guid, string>> NamesAsync(PortalDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Where(id => id != Guid.Empty).Distinct().ToList();
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
