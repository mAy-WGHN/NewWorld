namespace Portal.Infrastructure.Faqs;

/// <summary>
/// A FAQ entry. The answer is a rich-text document (<see cref="ContentJson"/>, TipTap JSON, checked by <c>FaqContent</c>); <see cref="Description"/> is the
/// same answer as Markdown for searching and for old clients (derived from the document; an entry that old clients wrote has only this text and no document yet).
/// <see cref="SortOrder"/> is exposed as "order" by the API.
/// </summary>
public sealed class FaqRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Title { get; set; }
    public required string Description { get; set; }
    public string? ContentJson { get; set; }
    public int SortOrder { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A file attached to a FAQ entry (PDF, image, Office document). The id is a random UUID; the file is only reachable through the API after the
/// sign-in. <see cref="FileName"/> is the cleaned name as shown and offered for download.
/// </summary>
public sealed class FaqAttachmentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FaqId { get; set; }
    public required string FileName { get; set; }
    public required string Extension { get; set; }
    public long Length { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// An uploaded image. The id is deliberately a random UUID (not version 7): it is the token in the public URL and the only access control.
/// </summary>
public sealed class FaqImageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Extension { get; set; }

    /// <summary>The entry that links the image in its Markdown (the one saved last wins); none = orphaned.</summary>
    public Guid? FaqId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
