using HrTool_API.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace HrTool_API.Tests.Unit;

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
        Assert.Equal("privilege:sample_thing:read", ((IAuthorizeData)new HasPrivilegeAttribute<SampleThing>(CrudAction.Read)).Policy);
        Assert.Equal("privilege:ro:export", ((IAuthorizeData)new HasPrivilegeAttribute<ReadOnlyThing>("export")).Policy);
        Assert.Throws<InvalidOperationException>(() => new HasPrivilegeAttribute<ReadOnlyThing>(CrudAction.Delete));
    }

    [Fact]
    public void TheApplicationsCatalog_DoesNotContainRightsOfOtherServices()
    {
        // Benutzer, Rollen, Scopes und Filialen gehören Rollen-Rechte, Meldungen dem Portal.
        var keys = PrivilegeCatalog.Areas.Select(a => a.Key).ToList();

        Assert.DoesNotContain("location", keys);
        Assert.DoesNotContain("role", keys);
        Assert.DoesNotContain("user", keys);
        Assert.DoesNotContain("report", keys);
    }

    [Fact]
    public void EveryEntityWithTheAttribute_IsRegisteredAsDbSet_SoItsRightsReallyExist()
    {
        var attributed = typeof(PrivilegeCatalog).Assembly.GetTypes()
            .Where(t => t.IsClass && t.GetCustomAttributes(typeof(PrivilegeResourceAttribute), false).Length > 0)
            .ToList();

        Assert.Empty(attributed.Except(PrivilegeCatalog.ResourceEntities).Select(t => t.Name));
    }
}
