using Portal.Authentication;
namespace Portal.Domain.Entities;

[PrivilegeResource("FAQ")]
public class Faq
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Title { get; set; } = string.Empty;

    /// <summary>Markdown.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Anzeigeposition, kleinster Wert zuerst. Das öffentliche API-Feld heißt trotzdem "order".
    /// </summary>
    public int SortOrder { get; set; }

    public bool Archived { get; set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
}
