using Rollen_Rechte.Authentication;
namespace Rollen_Rechte.Domain.Entities;

[PrivilegeResource("Filialen", DeleteLabel = "Archivieren", ScopedActions = CrudAction.All)]
public class Location
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;

    /// <summary>Eindeutig.</summary>
    public string StoreCode { get; set; } = string.Empty;

    /// <summary>Soft-Delete; archivierte Filialen bleiben von Meldungen referenziert.</summary>
    public bool Archived { get; set; }
}
