namespace Access.Infrastructure.People;

/// <summary>
/// The main store (Hauptfiliale) of a person as maintained in Access. It wins over the suggestion from BC (the "Vorgabefiliale",
/// which is not reliably maintained there); without a row the BC value counts. Owned by Access, so BC updates never overwrite it.
/// </summary>
public sealed class MainStoreOverrideRecord
{
    public Guid EmployeeId { get; set; }
    public Guid StoreId { get; set; }
    public Guid SetBy { get; set; }
    public DateTimeOffset SetAt { get; set; }
}
