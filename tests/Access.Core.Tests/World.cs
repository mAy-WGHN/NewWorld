using Access.Core.Acl;
using Access.Core.People;
using Access.Core.Responsibilities;

namespace Access.Core.Tests;

/// <summary>
/// The example from the design discussion: store F017 with Erika (sales) and Max (store manager), Tom (store manager F018,
/// substitutes Max 1–14 Aug), Sabine (district F017–F019), Petra (HR).
/// </summary>
internal sealed class World
{
    public static readonly Guid F017 = Guid.Parse("00000000-0000-0000-0000-000000000017");
    public static readonly Guid F018 = Guid.Parse("00000000-0000-0000-0000-000000000018");
    public static readonly Guid F019 = Guid.Parse("00000000-0000-0000-0000-000000000019");

    public static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    public static readonly Guid Max = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid Tom = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    public static readonly Guid Sabine = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    public static readonly Guid Petra = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    public static readonly Guid StoreManagerRole = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid DistrictRole = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid HrRole = Guid.Parse("10000000-0000-0000-0000-000000000003");

    public static readonly Guid DistrictSouth = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid StoreF017Scope = Guid.Parse("20000000-0000-0000-0000-000000000017");

    public List<Person> People { get; } =
    [
        new(Erika, "10042", "Erika Mustermann", F017, "VERKAUF", null),
        new(Max, "10001", "Max Muster", F017, "FILIALLEITUNG", null),
        new(Tom, "10002", "Tom Tester", F018, "FILIALLEITUNG", null),
        new(Sabine, "10003", "Sabine Beispiel", null, "BEZIRKSLEITUNG", null),
        new(Petra, "10004", "Petra Personal", null, "HR", null),
    ];

    public List<CatalogEntry> Catalog { get; } =
    [
        new("absence:read", "workforce", Scopable: true),
        new("absence:read-reason", "workforce", Scopable: true),
        new("absence:approve", "workforce", Scopable: true),
        new("absence:read-own", "workforce", Scopable: false),
        new("faq:read", "portal", Scopable: false),
        .. AccessPrivileges.Catalog,
    ];

    public List<Role> Roles { get; } =
    [
        Role.Administrator,
        Role.PublicUser with { Privileges = new HashSet<string> { "absence:read-own", "faq:read" } },
        new(StoreManagerRole, "Filialleitung", new HashSet<string> { "absence:read", "absence:read-reason", "absence:approve" }, BuiltIn: false),
        new(DistrictRole, "Bezirksleitung", new HashSet<string> { "absence:read" }, BuiltIn: false),
        new(HrRole, "HR", new HashSet<string> { "absence:*", "access:assignment:*" }, BuiltIn: false),
    ];

    public List<ScopeDefinition> Scopes { get; } =
    [
        new(DistrictSouth, "Bezirk Süd", new HashSet<Guid> { F017, F018, F019 }),
        new(StoreF017Scope, "Filiale F017", new HashSet<Guid> { F017 }),
    ];

    public List<Assignment> Assignments { get; } =
    [
        Assignment.Restricted(Max, StoreManagerRole, includeHomeStore: true),
        Assignment.Restricted(Tom, StoreManagerRole, includeHomeStore: true),
        Assignment.Restricted(Tom, StoreManagerRole, includeHomeStore: false, [StoreF017Scope],
            validFrom: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(2)), validTo: new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.FromHours(2))),
        Assignment.Restricted(Sabine, DistrictRole, includeHomeStore: false, [DistrictSouth]),
        Assignment.Unrestricted(Petra, HrRole),
    ];

    public List<Responsibility> Responsibilities { get; } = [];

    public AclData Data(TimeZoneInfo? zone = null) => new(People, Catalog, Roles, Scopes, Assignments, Responsibilities);
}
