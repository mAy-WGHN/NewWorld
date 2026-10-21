using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Contracts;
using Rollen_Rechte.Data;
using Rollen_Rechte.Domain.Entities;

namespace Rollen_Rechte.Controllers;

/// <summary>
/// Die anderen Anwendungen melden hier beim Start ihren Rechtekatalog an (<c>PUT /api/apps/{appKey}/catalog</c>, Schlüssel je Anwendung in
/// <c>Apps__Keys__{appKey}</c>). Dieser Dienst führt die Kataloge zusammen, damit man die Rechte in Rollen vergeben kann; durchgesetzt werden sie
/// von der Anwendung selbst. Ein Recht gehört genau einer Anwendung; Rechte, die dieser Dienst selbst erzeugt, sind gesperrt.
/// Außerdem beantwortet dieser Dienst für die Anwendungen, wer einen Vorgang sehen darf (<c>POST /api/apps/{appKey}/recipients</c>), z. B. für Benachrichtigungen.
/// </summary>
[Route("api/apps")]
[AllowAnonymous]
[AppKey]
public partial class AppsController(AccessDbContext db, PrivilegeCatalogService catalog, RecipientResolver recipients) : ApiControllerBase
{
    [GeneratedRegex("^[a-z][a-z0-9_-]{1,49}$")] private static partial Regex AppKeyFormat();
    [GeneratedRegex("^[a-z][a-z0-9_]*$")] private static partial Regex AreaKeyFormat();
    [GeneratedRegex("^[a-z][a-z0-9_]*:[a-z][a-z0-9_]*$")] private static partial Regex PrivilegeFormat();

    [HttpPut("{appKey}/catalog")]
    public async Task<IActionResult> Register(string appKey, RegisterCatalogRequest request, CancellationToken ct)
    {
        if (!AppKeyFormat().IsMatch(appKey)) throw ApiException.Validation("appKey", "Der Anwendungsschlüssel ist ungültig.");

        var wanted = new List<AppPrivilege>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in request.Areas!)
        {
            var areaKey = area.Key!.Trim();
            if (!AreaKeyFormat().IsMatch(areaKey)) throw ApiException.Validation("areas", $"Ungültiger Bereichsschlüssel: {areaKey}");

            foreach (var action in area.Actions!)
            {
                var privilege = action.Privilege!.Trim();
                if (!PrivilegeFormat().IsMatch(privilege) || !privilege.StartsWith(areaKey + ":", StringComparison.Ordinal))
                    throw ApiException.Validation("areas", $"Ungültiges Recht: {privilege} (erwartet: {areaKey}:aktion)");
                if (!seen.Add(privilege)) throw ApiException.Validation("areas", $"Das Recht kommt mehrfach vor: {privilege}");
                if (PrivilegeCatalog.All.Contains(privilege))
                    throw ApiException.Conflict($"Das Recht {privilege} gehört diesem Dienst und kann nicht registriert werden.");

                wanted.Add(new AppPrivilege
                {
                    AppKey = appKey, Privilege = privilege, Label = action.Label!.Trim(), Scopable = action.Scopable,
                    AreaKey = areaKey, AreaLabel = area.Label!.Trim(), DefinesReach = area.DefinesReach
                });
            }
        }

        var wantedKeys = wanted.Select(w => w.Privilege).ToList();
        var foreign = await db.AppPrivileges.Where(p => p.AppKey != appKey && wantedKeys.Contains(p.Privilege)).Select(p => p.Privilege).FirstOrDefaultAsync(ct);
        if (foreign is not null) throw ApiException.Conflict($"Das Recht {foreign} gehört schon einer anderen Anwendung.");
        var custom = await db.CustomPrivileges.Where(c => wantedKeys.Contains(c.Key)).Select(c => c.Key).FirstOrDefaultAsync(ct);
        if (custom is not null) throw ApiException.Conflict($"Das Recht {custom} existiert bereits als eigenes Recht.");

        // Ersetzen: vorhandene Einträge dieser Anwendung aktualisieren, neue anlegen, nicht mehr gemeldete entfernen.
        var existing = await db.AppPrivileges.Where(p => p.AppKey == appKey).ToListAsync(ct);
        db.AppPrivileges.RemoveRange(existing.Where(e => !wantedKeys.Contains(e.Privilege)));
        foreach (var item in wanted)
        {
            var current = existing.FirstOrDefault(e => e.Privilege == item.Privilege);
            if (current is null) db.AppPrivileges.Add(item);
            else
            {
                current.Label = item.Label;
                current.Scopable = item.Scopable;
                current.AreaKey = item.AreaKey;
                current.AreaLabel = item.AreaLabel;
                current.DefinesReach = item.DefinesReach;
            }
        }

        await db.SaveChangesAsync(ct);
        catalog.Invalidate();
        return Success(new RegisterCatalogResponse(appKey, wanted.Count));
    }

    /// <summary>Alle Filialen, auch archivierte, für Anwendungen, die Meldungen o. Ä. mit Filial-IDs speichern und Namen anzeigen oder IDs prüfen müssen (öffentlich ist nur die Liste der aktiven).</summary>
    [HttpGet("{appKey}/locations")]
    public async Task<IActionResult> Locations(string appKey, CancellationToken ct) =>
        Success(await db.Locations.AsNoTracking().OrderBy(l => l.StoreCode).Select(LocationResponse.Projection).ToListAsync(ct));

    /// <summary>
    /// Wer darf <c>privilege</c> in Bezug auf die genannten Filialen/Einzelobjekte? Antwort: die IDs der aktiven User (siehe <see cref="RecipientResolver"/>).
    /// Nur lesend; der Aufruf nutzt denselben Schlüssel wie die Registrierung der Anwendung.
    /// </summary>
    [HttpPost("{appKey}/recipients")]
    public async Task<IActionResult> Recipients(string appKey, ResolveRecipientsRequest request, CancellationToken ct) =>
        Success(new ResolveRecipientsResponse(await recipients.ResolveAsync(
            request.Privilege!.Trim(), request.LocationIds ?? [], request.ReportIds ?? [], request.UserIds, ct)));
}
