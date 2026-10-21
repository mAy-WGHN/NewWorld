using Rollen_Rechte.Authentication;
using Rollen_Rechte.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace Rollen_Rechte.Tests.Unit;

public class PrivilegeCatalogTests
{
    // Beispiel-Entities: Das Attribut allein erzeugt den Berechtigungssatz.
    [PrivilegeResource("Beispiele")]
    private sealed class SampleThing;

    [PrivilegeResource("Nur lesbar", Key = "ro", Actions = CrudAction.Read | CrudAction.Update, DeleteLabel = "Entfernen")]
    [PrivilegeAction("publish", "Veröffentlichen")]
    [PrivilegeAction("export", "Exportieren")]
    private sealed class ReadOnlyThing;

    private sealed class NoResource;

    [Fact]
    public void Attribute_GeneratesTheFullCrudSet_WithAKeyFromTheClassName()
    {
        var area = PrivilegeCatalog.AreaFor(typeof(SampleThing));

        Assert.Equal("sample_thing", area.Key);
        Assert.Equal("Beispiele", area.Label);
        Assert.Equal(
            ["sample_thing:read", "sample_thing:create", "sample_thing:update", "sample_thing:delete"],
            area.Actions.Select(a => a.Privilege));
        Assert.Equal(["Lesen", "Erstellen", "Ändern", "Löschen"], area.Actions.Select(a => a.Label));
    }

    [Fact]
    public void Attribute_CanRestrictActions_RenameDelete_AndAddExtraActions()
    {
        var area = PrivilegeCatalog.AreaFor(typeof(ReadOnlyThing));

        Assert.Equal("ro", area.Key);
        Assert.Equal(["ro:read", "ro:update", "ro:publish", "ro:export"], area.Actions.Select(a => a.Privilege));
        Assert.Equal("Veröffentlichen", area.Actions.Single(a => a.Action == "publish").Label);
        Assert.Equal("publish", area.Actions.Single(a => a.Privilege == "ro:publish").Action);
    }

    [Fact]
    public void PrivilegeOf_ReturnsTheKey_AndRejectsActionsTheEntityDoesNotHave()
    {
        Assert.Equal("sample_thing:update", PrivilegeCatalog.PrivilegeOf(typeof(SampleThing), CrudAction.Update));
        Assert.Equal("ro:export", PrivilegeCatalog.PrivilegeOf(typeof(ReadOnlyThing), "export"));

        Assert.Throws<InvalidOperationException>(() => PrivilegeCatalog.PrivilegeOf(typeof(ReadOnlyThing), CrudAction.Delete));
        Assert.Throws<InvalidOperationException>(() => PrivilegeCatalog.PrivilegeOf(typeof(ReadOnlyThing), "nope"));
        Assert.Throws<ArgumentException>(() => PrivilegeCatalog.PrivilegeOf(typeof(SampleThing), CrudAction.Read | CrudAction.Create));
        Assert.Throws<InvalidOperationException>(() => PrivilegeCatalog.AreaFor(typeof(NoResource)));
    }

    [Fact]
    public void HasPrivilegeAttribute_BuildsThePolicyFromTheEntity()
    {
        Assert.Equal("privilege:user:read", ((IAuthorizeData)new HasPrivilegeAttribute<User>(CrudAction.Read)).Policy);
        Assert.Equal("privilege:role:assign", ((IAuthorizeData)new HasPrivilegeAttribute<AclRole>("assign")).Policy);
        Assert.Throws<InvalidOperationException>(() => new HasPrivilegeAttribute<ReadOnlyThing>(CrudAction.Delete)); // Aktion nicht freigegeben
    }

    [Fact]
    public void TheEntitiesOfThisService_GetTheirRightsFromTheAttribute()
    {
        var areas = PrivilegeCatalog.Areas.ToDictionary(a => a.Key);

        Assert.Equal(["user:read", "user:create", "user:update", "user:delete"], areas["user"].Actions.Select(a => a.Privilege));
        Assert.Equal("Deaktivieren", areas["user"].Actions.Single(a => a.Action == "delete").Label);
        Assert.Equal(["location:read", "location:create", "location:update", "location:delete"], areas["location"].Actions.Select(a => a.Privilege));
        Assert.Equal(["role:read", "role:create", "role:update", "role:delete", "role:assign"], areas["role"].Actions.Select(a => a.Privilege));
        Assert.Contains("scope:create", PrivilegeCatalog.All);
        Assert.Contains("custom_privilege:delete", PrivilegeCatalog.All);
        // Die Rechte des Portals (Meldungen, FAQ, Statistik) kommen erst mit der Registrierung der Anwendung.
        Assert.DoesNotContain("report:read", PrivilegeCatalog.All);
        Assert.DoesNotContain("faq:read", PrivilegeCatalog.All);
    }

    [Fact]
    public void EveryEntityWithTheAttribute_IsRegisteredAsDbSet_SoItsRightsReallyExist()
    {
        var attributed = typeof(PrivilegeCatalog).Assembly.GetTypes()
            .Where(t => t.IsClass && t.GetCustomAttributes(typeof(PrivilegeResourceAttribute), false).Length > 0)
            .ToList();

        Assert.NotEmpty(attributed);
        Assert.Empty(attributed.Except(PrivilegeCatalog.ResourceEntities).Select(t => t.Name));
    }

    [Fact]
    public void LinkAndHelperEntities_HaveNoRights()
    {
        var keys = PrivilegeCatalog.Areas.Select(a => a.Key).ToList();

        Assert.DoesNotContain("acl_user_role", keys);
        Assert.DoesNotContain("refresh_token", keys);
    }

    [Fact]
    public void Snapshot_MergesCustomRightsIntoTheirAreas_AndIgnoresKeysOfGeneratedRights()
    {
        var snapshot = new PrivilegeSnapshot([("user:export", "Exportieren"), ("lager:zaehlen", "Zählen"), ("user:read", "doppelt")]);

        var area = snapshot.Areas.Single(a => a.Key == "user");
        Assert.Equal("Benutzer", area.Label);
        Assert.Equal(["user:read", "user:create", "user:update", "user:delete", "user:export"], area.Actions.Select(a => a.Privilege));
        Assert.True(area.Actions.Single(a => a.Privilege == "user:export").Custom);
        Assert.False(area.Actions.Single(a => a.Privilege == "user:read").Custom);

        Assert.Equal("lager", snapshot.Areas.Single(a => a.Key == "lager").Label);
        Assert.Equal(["lager:zaehlen", "user:export"], snapshot.Custom.Order());
        Assert.True(snapshot.IsKnown("lager:zaehlen"));
        Assert.True(snapshot.IsKnown("lager:*"));
        Assert.False(snapshot.IsKnown("lager:nope"));
        Assert.False(new PrivilegeSnapshot([]).IsKnown("lager:*"));
    }

    [Fact]
    public void Snapshot_MergesTheRightsOfRegisteredApplications_WithTheirScopability()
    {
        var apps = new[]
        {
            new AppPrivilege { AppKey = "portal", Privilege = "report:read", Label = "Lesen", AreaKey = "report", AreaLabel = "Meldungen", Scopable = true },
            new AppPrivilege { AppKey = "portal", Privilege = "report:update", Label = "Ändern", AreaKey = "report", AreaLabel = "Meldungen", Scopable = true },
            new AppPrivilege { AppKey = "portal", Privilege = "faq:create", Label = "Erstellen", AreaKey = "faq", AreaLabel = "FAQ", DefinesReach = false },
            new AppPrivilege { AppKey = "portal", Privilege = "user:read", Label = "gehört schon diesem Dienst", AreaKey = "user", AreaLabel = "x" }
        };

        var snapshot = new PrivilegeSnapshot([], apps);

        var report = snapshot.Areas.Single(a => a.Key == "report");
        Assert.Equal("Meldungen", report.Label);
        Assert.Equal(["report:read", "report:update"], report.Actions.Select(a => a.Privilege));
        Assert.True(snapshot.IsScopable("report:read"));
        Assert.False(snapshot.IsScopable("faq:create"));
        Assert.True(snapshot.IsScopable("user:read")); // generiert, nicht überschrieben
        Assert.Equal("Benutzer", snapshot.Areas.Single(a => a.Key == "user").Label);
        Assert.True(snapshot.DefinesReach(["report:*"]));
        Assert.False(snapshot.DefinesReach(["faq:create"])); // reine Verwaltung zählt nicht als Datenreichweite
        Assert.True(snapshot.IsKnown("report:*"));
        Assert.Contains("report:update", snapshot.Expand(["report:*"]));
    }

    [Fact]
    public void Snapshot_ExpandsWildcardsOverCustomRights()
    {
        var snapshot = new PrivilegeSnapshot([("user:export", "Exportieren")]);

        Assert.Contains("user:export", snapshot.Expand(["user:*"]));
        Assert.Contains("user:export", snapshot.Expand(["*"]));
        Assert.DoesNotContain("user:export", snapshot.Expand(["user:read"]));
        Assert.Equal(["user:export"], snapshot.Expand(["user:export", "user:unbekannt"]));
    }
}
