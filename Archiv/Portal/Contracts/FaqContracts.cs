using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Portal.Domain.Entities;

namespace Portal.Contracts;

/// <summary>Das API-Feld heißt "order" (Entity-Property: SortOrder), das Frontend erwartet diesen Namen.</summary>
public record FaqResponse(Guid Id, string Title, string Description, int Order, bool Archived, DateTime CreatedAt)
{
    public static readonly Expression<Func<Faq, FaqResponse>> Projection =
        f => new FaqResponse(f.Id, f.Title, f.Description, f.SortOrder, f.Archived, f.CreatedAt);

    public static FaqResponse From(Faq f) => new(f.Id, f.Title, f.Description, f.SortOrder, f.Archived, f.CreatedAt);
}

public record CreateFaqRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(255, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 255 Zeichen enthalten.")]
    public string? Title { get; init; }

    /// <summary>Markdown.</summary>
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public string? Description { get; init; }
}

public record UpdateFaqRequest
{
    [MaxLength(255, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 255 Zeichen enthalten.")]
    public string? Title { get; init; }

    public string? Description { get; init; }
    public bool? Archived { get; init; }
}

public record ReorderFaqRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MinLength(1, ErrorMessage = "Es muss mindestens ein Eintrag angegeben werden.")]
    public List<Guid>? OrderedIds { get; init; }
}

public record FaqImageUploadResponse(Guid Token, string Url);

/// <summary>Status: orphaned = keine FAQ, archived = nur archivierte FAQ, active = aktive FAQ.</summary>
public record FaqImageResponse(Guid Id, string Url, DateTime CreatedAt, string Status, Guid? FaqId, string? FaqTitle);
