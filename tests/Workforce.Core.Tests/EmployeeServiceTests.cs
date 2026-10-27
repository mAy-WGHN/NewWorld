using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Workforce.Core.Absences;
using Workforce.Core.Employees;

namespace Workforce.Core.Tests;

internal sealed class FakeCards : IEmployeeCardStore
{
    public Dictionary<Guid, EmployeeCard> Cards { get; } = [];
    public HashSet<string> NumbersOfBcCopies { get; } = [];
    public HashSet<Guid> ActiveStores { get; } = [];
    public List<Guid> CopiesTouched { get; } = [];

    public Task<EmployeeCard?> GetAsync(Guid employeeId, CancellationToken cancellationToken) => Task.FromResult(Cards.GetValueOrDefault(employeeId));

    public void Add(EmployeeCard card)
    {
        Cards[card.EmployeeId] = card;
        CopiesTouched.Add(card.EmployeeId);
    }

    public Task CardChangedAsync(EmployeeCard card, CancellationToken cancellationToken)
    {
        CopiesTouched.Add(card.EmployeeId);
        return Task.CompletedTask;
    }

    public Task<bool> PersonnelNumberExistsAsync(string personnelNumber, CancellationToken cancellationToken) =>
        Task.FromResult(NumbersOfBcCopies.Contains(personnelNumber) || Cards.Values.Any(c => c.PersonnelNumber == personnelNumber));

    public Task<Guid?> FindByPersonnelNumberAsync(string personnelNumber, CancellationToken cancellationToken) =>
        Task.FromResult(Cards.Values.FirstOrDefault(c => c.PersonnelNumber == personnelNumber)?.EmployeeId);

    public Task<bool> StoreIsActiveAsync(Guid storeId, CancellationToken cancellationToken) => Task.FromResult(ActiveStores.Contains(storeId));
}

internal sealed class FakeLists : IReferenceLists
{
    public List<ReferenceEntry> Entries { get; } = [];

    public Task<IReadOnlyList<ReferenceEntry>> ListAsync(ReferenceKind kind, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReferenceEntry>>([.. Entries.Where(e => e.Kind == kind)]);

    public Task<ReferenceEntry?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Entries.FirstOrDefault(e => e.Id == id));

    public void Add(ReferenceEntry entry) => Entries.Add(entry);
}

internal sealed class FakeSeries : IPersonnelNumberSeries
{
    public long? Last { get; set; }

    public Task<long?> LastAsync(CancellationToken cancellationToken) => Task.FromResult(Last);

    public Task SetLastAsync(long last, Guid changedBy, DateTimeOffset at, CancellationToken cancellationToken)
    {
        Last = last;
        return Task.CompletedTask;
    }

    public Task<long> NextAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        Last = (Last ?? 0) + 1;
        return Task.FromResult(Last.Value);
    }
}

public class EmployeeServiceTests
{
    private static readonly Guid Hr = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid StoreA = Guid.Parse("00000000-0000-0000-0000-00000000a017");
    private static readonly Guid StoreInactive = Guid.Parse("00000000-0000-0000-0000-00000000a099");

    private readonly FakeCards _cards = new();
    private readonly FakeLists _lists = new();
    private readonly FakeSeries _series = new() { Last = 10041 };
    private readonly FakeEntitlements _entitlements = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero));

    private readonly ReferenceEntry _function = Entry(ReferenceKind.Function, "KASSE", "Kassierer");
    private readonly ReferenceEntry _wage = Entry(ReferenceKind.WageType, "L1", "Stundenlohn");

    public EmployeeServiceTests()
    {
        _cards.ActiveStores.Add(StoreA);
        _lists.Entries.AddRange([_function, _wage, Entry(ReferenceKind.Region, "SUED", "Süd")]);
    }

    private EmployeeService Service() => new(_cards, _lists, _series, _entitlements, _time, new WorkforceOptions());

    private static ReferenceEntry Entry(ReferenceKind kind, string code, string name, bool active = true) =>
        new() { Id = Guid.NewGuid(), Kind = kind, Code = code, Name = name, IsActive = active };

    private static DateOnly D(string date) => DateOnly.Parse(date, CultureInfo.InvariantCulture);

    private EmployeeData Data(Func<EmployeeData, EmployeeData>? change = null)
    {
        var data = new EmployeeData(
            "Erika", "Mustermann", null, Gender.Female, D("1990-05-17"), "Hauptstr. 1", "86150", "Augsburg", "+49 821 123456", "erika@privat.example", "erika@firma.example",
            StoreA, _function.Id, null, null, null, null, null, null, _wage.Id, D("2026-12-01"), null, null, QtradoExport: false);
        return change is null ? data : change(data);
    }

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task A_new_employee_gets_the_next_number_and_the_defaults()
    {
        var (error, card) = await Service().CreateAsync(Data(), 25, Hr, CancellationToken.None);

        error.ShouldBe(EmployeeError.None);
        card!.PersonnelNumber.ShouldBe("10042");
        _series.Last.ShouldBe(10042);
        (card.ReceiptName, card.State, card.WebTimeTrackingAllowed, card.PasswordChangeRequired).ShouldBe(("Mustermann", CardState.PendingBc, true, true));
        (card.CreatedBy, card.CreatedAt, card.ModifiedAt).ShouldBe((Hr, _time.GetUtcNow(), _time.GetUtcNow()));
        card.FullName.ShouldBe("Erika Mustermann");
        _cards.Cards[card.EmployeeId].ShouldBeSameAs(card);
        _cards.CopiesTouched.ShouldContain(card.EmployeeId);
    }

    [Fact]
    public async Task The_receipt_name_can_differ_from_the_last_name()
    {
        var (_, card) = await Service().CreateAsync(Data(d => d with { ReceiptName = " Erika M. " }), null, Hr, CancellationToken.None);

        card!.ReceiptName.ShouldBe("Erika M.");
    }

    [Fact]
    public async Task Two_new_employees_get_consecutive_numbers()
    {
        var first = await Service().CreateAsync(Data(), null, Hr, CancellationToken.None);
        var second = await Service().CreateAsync(Data(d => d with { FirstName = "Max" }), null, Hr, CancellationToken.None);

        (first.Card!.PersonnelNumber, second.Card!.PersonnelNumber).ShouldBe(("10042", "10043"));
        first.Card.EmployeeId.ShouldNotBe(second.Card.EmployeeId);
    }

    [Fact]
    public async Task The_vacation_entitlement_is_set_for_the_year_of_hiring()
    {
        var (_, card) = await Service().CreateAsync(Data(), 25, Hr, CancellationToken.None);

        _entitlements.Days[(card!.EmployeeId, 2026)].ShouldBe(25m);
    }

    [Fact]
    public async Task Without_an_entitlement_none_is_set()
    {
        var (_, card) = await Service().CreateAsync(Data(), null, Hr, CancellationToken.None);

        _entitlements.Days.Keys.ShouldNotContain(k => k.Item1 == card!.EmployeeId);
    }

    [Fact]
    public async Task Nobody_can_be_created_before_HR_has_entered_the_last_used_number()
    {
        _series.Last = null;

        var (error, card) = await Service().CreateAsync(Data(), null, Hr, CancellationToken.None);

        (error, card).ShouldBe((EmployeeError.NumberSeriesNotSet, null));
        _cards.Cards.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_number_that_a_person_already_has_is_refused()
    {
        _cards.NumbersOfBcCopies.Add("10042"); // somebody was created directly in BC

        (await Service().CreateAsync(Data(), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.DuplicateNumber);
        _cards.Cards.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- validation

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Names_are_required(string name)
    {
        (await Service().CreateAsync(Data(d => d with { FirstName = name }), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.Invalid);
        (await Service().CreateAsync(Data(d => d with { LastName = name }), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.Invalid);
    }

    [Fact]
    public async Task Names_have_a_sensible_length() =>
        (await Service().CreateAsync(Data(d => d with { FirstName = new string('a', 101) }), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.Invalid);

    [Fact]
    public async Task The_gender_must_be_a_known_value() =>
        (await Service().CreateAsync(Data(d => d with { Gender = Gender.Unknown }), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.Invalid);

    [Theory]
    [InlineData("2026-12-01", "2026-11-30", null)]   // exit before hire
    [InlineData("1999-12-31", null, null)]            // hire date out of range
    [InlineData("2026-12-01", null, "2026-12-02")]    // born after hiring
    [InlineData("2026-12-01", null, "2027-01-01")]    // born in the future
    [InlineData("2026-12-01", null, "1899-12-31")]    // implausible birth date
    public async Task The_dates_must_fit_together(string hire, string? exit, string? birth)
    {
        var data = Data(d => d with { HireDate = D(hire), ExitDate = exit is null ? null : D(exit), BirthDate = birth is null ? d.BirthDate : D(birth) });

        (await Service().CreateAsync(data, null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.Invalid);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("two@@example.com")]
    public async Task E_mail_addresses_must_look_like_one(string email)
    {
        (await Service().CreateAsync(Data(d => d with { PrivateEmail = email }), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.Invalid);
        (await Service().CreateAsync(Data(d => d with { BusinessEmail = email }), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.Invalid);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12")]
    public async Task Phone_numbers_must_look_like_one(string phone) =>
        (await Service().CreateAsync(Data(d => d with { PrivatePhone = phone }), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.Invalid);

    [Fact]
    public async Task Optional_contact_data_may_be_left_out()
    {
        var data = Data(d => d with { Street = null, PostalCode = null, City = null, PrivatePhone = null, PrivateEmail = null, BusinessEmail = null, BirthDate = null });

        (await Service().CreateAsync(data, null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.None);
    }

    [Fact]
    public async Task An_entitlement_must_be_sensible()
    {
        (await Service().CreateAsync(Data(), -1, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.Invalid);
        (await Service().CreateAsync(Data(), 400, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.Invalid);
    }

    // ---------------------------------------------------------------- references, store, supervisor

    [Fact]
    public async Task A_selection_must_exist_belong_to_its_list_and_be_active()
    {
        var inactive = Entry(ReferenceKind.Function, "ALT", "Alt", active: false);
        _lists.Entries.Add(inactive);

        (await Service().CreateAsync(Data(d => d with { FunctionId = Guid.NewGuid() }), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.UnknownReference);
        (await Service().CreateAsync(Data(d => d with { FunctionId = _wage.Id }), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.UnknownReference); // wage type in the function field
        (await Service().CreateAsync(Data(d => d with { FunctionId = inactive.Id }), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.UnknownReference);
        (await Service().CreateAsync(Data(d => d with { RegionId = _lists.Entries.Single(e => e.Kind == ReferenceKind.Region).Id }), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.None);
    }

    [Fact]
    public async Task The_main_store_must_be_a_known_active_store()
    {
        _cards.ActiveStores.Remove(StoreA);

        (await Service().CreateAsync(Data(), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.UnknownStore);
        (await Service().CreateAsync(Data(d => d with { MainStoreId = null }), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.None); // head office: no store
    }

    [Fact]
    public async Task The_supervisor_is_given_by_personnel_number_and_must_exist()
    {
        (await Service().CreateAsync(Data(d => d with { SupervisorPersonnelNumber = "99999" }), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.UnknownSupervisor);

        var boss = (await Service().CreateAsync(Data(d => d with { FirstName = "Chef" }), null, Hr, CancellationToken.None)).Card!;
        var employee = (await Service().CreateAsync(Data(d => d with { SupervisorPersonnelNumber = boss.PersonnelNumber }), null, Hr, CancellationToken.None)).Card!;

        employee.SupervisorId.ShouldBe(boss.EmployeeId);
    }

    // ---------------------------------------------------------------- change

    [Fact]
    public async Task A_change_replaces_the_fields_but_never_the_personnel_number()
    {
        var card = (await Service().CreateAsync(Data(), null, Hr, CancellationToken.None)).Card!;
        _time.Advance(TimeSpan.FromDays(1));

        var error = await Service().UpdateAsync(card.EmployeeId, Data(d => d with { City = "München", ExitDate = D("2027-06-30"), QtradoExport = true }), mayChangeWageType: true, Hr, CancellationToken.None);

        error.ShouldBe(EmployeeError.None);
        (card.PersonnelNumber, card.City, card.ExitDate, card.QtradoExport, card.ModifiedAt, card.CreatedAt).ShouldBe(("10042", "München", D("2027-06-30"), true, _time.GetUtcNow(), _time.GetUtcNow().AddDays(-1)));
        _cards.CopiesTouched.Count(id => id == card.EmployeeId).ShouldBe(2); // created, then the copy of the person was kept in step
    }

    [Fact]
    public async Task The_wage_type_can_only_be_changed_by_those_who_may_see_it()
    {
        var card = (await Service().CreateAsync(Data(), null, Hr, CancellationToken.None)).Card!;
        var other = Entry(ReferenceKind.WageType, "L2", "Monatslohn");
        _lists.Entries.Add(other);

        (await Service().UpdateAsync(card.EmployeeId, Data(d => d with { WageTypeId = other.Id }), mayChangeWageType: false, Hr, CancellationToken.None)).ShouldBe(EmployeeError.NotAllowed);
        card.WageTypeId.ShouldBe(_wage.Id);
        // Everything else may change without the right, as long as the wage type stays.
        (await Service().UpdateAsync(card.EmployeeId, Data(d => d with { City = "Ulm" }), mayChangeWageType: false, Hr, CancellationToken.None)).ShouldBe(EmployeeError.None);
        (await Service().UpdateAsync(card.EmployeeId, Data(d => d with { WageTypeId = other.Id }), mayChangeWageType: true, Hr, CancellationToken.None)).ShouldBe(EmployeeError.None);
        card.WageTypeId.ShouldBe(other.Id);
    }

    [Fact]
    public async Task A_change_of_an_unknown_card_is_not_found() =>
        (await Service().UpdateAsync(Guid.NewGuid(), Data(), true, Hr, CancellationToken.None)).ShouldBe(EmployeeError.NotFound);

    [Fact]
    public async Task A_change_is_validated_like_a_creation()
    {
        var card = (await Service().CreateAsync(Data(), null, Hr, CancellationToken.None)).Card!;

        (await Service().UpdateAsync(card.EmployeeId, Data(d => d with { LastName = "" }), true, Hr, CancellationToken.None)).ShouldBe(EmployeeError.Invalid);
        (await Service().UpdateAsync(card.EmployeeId, Data(d => d with { FunctionId = Guid.NewGuid() }), true, Hr, CancellationToken.None)).ShouldBe(EmployeeError.UnknownReference);
        card.LastName.ShouldBe("Mustermann");
    }

    [Fact]
    public async Task A_card_is_active_between_hiring_and_exit()
    {
        var card = (await Service().CreateAsync(Data(d => d with { ExitDate = D("2027-06-30") }), null, Hr, CancellationToken.None)).Card!;

        card.IsActiveOn(D("2026-11-30")).ShouldBeFalse();
        card.IsActiveOn(D("2026-12-01")).ShouldBeTrue();
        card.IsActiveOn(D("2027-06-30")).ShouldBeTrue();
        card.IsActiveOn(D("2027-07-01")).ShouldBeFalse();
    }

    // ---------------------------------------------------------------- number series

    [Fact]
    public async Task HR_sets_the_last_used_number()
    {
        (await Service().SetLastNumberAsync(20000, Hr, CancellationToken.None)).ShouldBe(EmployeeError.None);

        (await Service().LastNumberAsync(CancellationToken.None)).ShouldBe(20000);
        (await Service().CreateAsync(Data(), null, Hr, CancellationToken.None)).Card!.PersonnelNumber.ShouldBe("20001");
    }

    [Fact]
    public async Task The_last_number_cannot_be_negative() =>
        (await Service().SetLastNumberAsync(-1, Hr, CancellationToken.None)).ShouldBe(EmployeeError.Invalid);

    // ---------------------------------------------------------------- selection lists

    [Fact]
    public async Task HR_adds_entries_to_a_list_codes_are_unique_per_list_regardless_of_case()
    {
        var added = await Service().AddEntryAsync(ReferenceKind.Workplace, " FIL17 ", "Filiale 17", CancellationToken.None);
        added.Error.ShouldBe(EmployeeError.None);
        (added.Entry!.Code, added.Entry.Name, added.Entry.IsActive).ShouldBe(("FIL17", "Filiale 17", true));

        (await Service().AddEntryAsync(ReferenceKind.Workplace, "fil17", "Doppelt", CancellationToken.None)).Error.ShouldBe(EmployeeError.DuplicateCode);
        (await Service().AddEntryAsync(ReferenceKind.Region, "FIL17", "Anderer Typ", CancellationToken.None)).Error.ShouldBe(EmployeeError.None); // another list
    }

    [Theory]
    [InlineData("", "Name")]
    [InlineData("CODE", "")]
    public async Task An_entry_needs_a_code_and_a_name(string code, string name) =>
        (await Service().AddEntryAsync(ReferenceKind.Workplace, code, name, CancellationToken.None)).Error.ShouldBe(EmployeeError.Invalid);

    [Fact]
    public async Task An_entry_needs_a_known_list() =>
        (await Service().AddEntryAsync(ReferenceKind.Unknown, "X", "Y", CancellationToken.None)).Error.ShouldBe(EmployeeError.Invalid);

    [Fact]
    public async Task An_entry_is_renamed_and_deactivated_never_deleted_and_a_deactivated_one_cannot_be_chosen_any_more()
    {
        (await Service().UpdateEntryAsync(_function.Id, "Kassiererin", isActive: false, CancellationToken.None)).ShouldBe(EmployeeError.None);

        (_function.Name, _function.IsActive).ShouldBe(("Kassiererin", false));
        (await Service().CreateAsync(Data(), null, Hr, CancellationToken.None)).Error.ShouldBe(EmployeeError.UnknownReference);
        (await Service().UpdateEntryAsync(Guid.NewGuid(), "X", true, CancellationToken.None)).ShouldBe(EmployeeError.NotFound);
    }

    [Fact]
    public async Task A_card_that_keeps_a_deactivated_entry_can_still_be_changed()
    {
        var card = (await Service().CreateAsync(Data(), null, Hr, CancellationToken.None)).Card!;
        await Service().UpdateEntryAsync(_function.Id, "Kassierer", isActive: false, CancellationToken.None);

        // The function was chosen when it was active; changing the city must not fail because of it.
        (await Service().UpdateAsync(card.EmployeeId, Data(d => d with { City = "Ulm" }), true, Hr, CancellationToken.None)).ShouldBe(EmployeeError.None);
    }
}
