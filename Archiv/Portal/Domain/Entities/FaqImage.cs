namespace Portal.Domain.Entities;

public class FaqImage
{
    /// <summary>
    /// Bewusst UUIDv4 (nicht v7): Die ID ist zugleich das unratbare Token in der öffentlichen URL
    /// und die einzige Zugriffskontrolle. Nicht auf v7/sequenzielle IDs umstellen.
    /// </summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Extension { get; set; } = string.Empty;
    public Guid? FaqId { get; set; }
    public Faq? Faq { get; set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
}
