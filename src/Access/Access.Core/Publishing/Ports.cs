using Access.Core.Acl;

namespace Access.Core.Publishing;

/// <summary>Loads all data the calculations need.</summary>
public interface IAclReader
{
    Task<AclData> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>Last published version and fingerprint per person and kind.</summary>
public interface IPublicationStore
{
    Task<IReadOnlyDictionary<(Guid EmployeeId, PublicationKind Kind), Publication>> LoadAllAsync(CancellationToken cancellationToken);

    Task SaveAsync(Guid employeeId, PublicationKind kind, Publication publication, CancellationToken cancellationToken);
}
