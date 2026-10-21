using Portal.Authentication;
using Portal.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace Portal.Tests.Unit;

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
        Assert.Equal("privilege:report:read", ((IAuthorizeData)new HasPrivilegeAttribute<Report>(CrudAction.Read)).Policy);
        Assert.Equal("privilege:ro:export", ((IAuthorizeData)new HasPrivilegeAttribute<ReadOnlyThing>("export")).Policy);
        Assert.Throws<InvalidOperationException>(() => new HasPrivilegeAttribute<Report>(CrudAction.Create)); // Anlegen ist öffentlich
    }

    [Fact]
    public void TheApplicationsEntities_GetTheirRightsFromTheAttribute()
    {
        var areas = PrivilegeCatalog.Areas.ToDictionary(a => a.Key);

        Assert.Equal(["report:read", "report:update", "report:delete"], areas["report"].Actions.Select(a => a.Privilege));
        Assert.Equal("Archivieren", areas["report"].Actions.Single(a => a.Action == "delete").Label);
        Assert.Equal(["faq:read", "faq:create", "faq:update", "faq:delete"], areas["faq"].Actions.Select(a => a.Privilege));
        Assert.All(areas["report"].Actions, a => Assert.True(a.Scopable));
        Assert.DoesNotContain(areas["faq"].Actions, a => a.Scopable);
        Assert.DoesNotContain("location", areas.Keys); // Filialen, User und Rollen gehören Rollen-Rechte
        Assert.DoesNotContain("role", areas.Keys);
        Assert.Contains("faq:delete", PrivilegeCatalog.All);
        Assert.Contains("statistic:read", PrivilegeCatalog.All);
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

        Assert.DoesNotContain("report_location", keys);
    }

}
