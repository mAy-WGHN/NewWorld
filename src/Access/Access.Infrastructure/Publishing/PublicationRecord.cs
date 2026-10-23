namespace Access.Infrastructure.Publishing;

/// <summary>Last published version and fingerprint per person and kind (permissions, responsibilities).</summary>
public sealed class PublicationRecord
{
    public Guid EmployeeId { get; set; }
    public required string Kind { get; set; }
    public long Version { get; set; }
    public required string Fingerprint { get; set; }
}
