using Microsoft.Extensions.Time.Testing;
using Notifications.Contracts;
using Workforce.Core.Employees;

namespace Workforce.Core.Tests;

internal sealed class FakeChangeStore : IEmployeeChangeStore
{
    public List<EmployeeChangeRequest> Requests { get; } = [];

    public void Add(EmployeeChangeRequest request) => Requests.Add(request);

    public Task<EmployeeChangeRequest?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Requests.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<EmployeeChangeRequest>> ForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmployeeChangeRequest>>([.. Requests.Where(r => r.EmployeeId == employeeId).OrderByDescending(r => r.CreatedAt)]);

    public Task<IReadOnlyList<EmployeeChangeRequest>> PendingAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmployeeChangeRequest>>([.. Requests.Where(r => r.Status == ChangeRequestStatus.Pending).OrderBy(r => r.CreatedAt)]);
}

/// <summary>A person never changes the own data: they ask, HR decides. Messages carry no data.</summary>
public class EmployeeChangeServiceTests
{
    private static readonly Guid Hr = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid Hr2 = Guid.Parse("00000000-0000-0000-0000-0000000000d2");

    private readonly FakeCards _cards = new();
    private readonly FakeChangeStore _requests = new();
    private readonly FakeResponsibilities _responsibilities = new();
    private readonly RecordingMessages _messages = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero));
    private readonly EmployeeCard _card;

    public EmployeeChangeServiceTests()
    {
        _card = new EmployeeCard
        {
            EmployeeId = Guid.NewGuid(), PersonnelNumber = "15302", FirstName = "Erika", LastName = "Neu", ReceiptName = "Neu",
            Street = "Hauptstr. 1", PostalCode = "86150", City = "Augsburg", PrivatePhone = "+49 821 123456", PrivateEmail = "erika@privat.example",
            HireDate = new DateOnly(2026, 12, 1), CreatedAt = _time.GetUtcNow(), ModifiedAt = _time.GetUtcNow(),
        };
        _cards.Cards[_card.EmployeeId] = _card;
        _responsibilities.ChangeRequestHandlers[_card.EmployeeId] = [Hr];
    }

    private EmployeeChangeService Service() => new(_requests, _cards, _responsibilities, _messages, _time);

    private static ChangeRequestFields Fields(string? street = null, string? postalCode = null, string? city = null, string? phone = null, string? email = null) =>
        new(street, postalCode, city, phone, email);

    private async Task<EmployeeChangeRequest> Pending(ChangeRequestFields? fields = null)
    {
        var (error, request) = await Service().RequestAsync(_card.EmployeeId, fields ?? Fields(street: "Neue Str. 5", city: "Ulm"), null, CancellationToken.None);
        error.ShouldBe(EmployeeError.None);
        _messages.Sent.Clear();
        return request!;
    }

    // ---------------------------------------------------------------- the person asks

    [Fact]
    public async Task A_request_is_stored_as_pending_notifies_HR_without_data_and_changes_nothing_on_the_card()
    {
        var (error, request) = await Service().RequestAsync(_card.EmployeeId, Fields(street: "Neue Str. 5", city: "Ulm"), "Umzug zum 1.12.", CancellationToken.None);

        error.ShouldBe(EmployeeError.None);
        (request!.Status, request.Street, request.City, request.PostalCode, request.Message, request.CreatedAt).ShouldBe((ChangeRequestStatus.Pending, "Neue Str. 5", "Ulm", null, "Umzug zum 1.12.", _time.GetUtcNow()));
        (_card.Street, _card.City).ShouldBe(("Hauptstr. 1", "Augsburg")); // the person cannot change the own data, not even the address
        var notification = _messages.Of<SendNotification>().Single();
        notification.TemplateKey.ShouldBe(EmployeeChangeService.RequestTemplate);
        notification.RecipientEmployeeIds.ShouldBe([Hr]);
        notification.Channels.ShouldBe([NotificationChannel.InApp, NotificationChannel.Push], ignoreOrder: true);
        notification.Data.ShouldBeEmpty();
        notification.DeepLink.ShouldBe($"/employee-change-requests/{request.Id}");
        _messages.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task All_responsible_persons_in_HR_are_notified_but_never_the_requester()
    {
        _responsibilities.ChangeRequestHandlers[_card.EmployeeId] = [Hr, Hr2, _card.EmployeeId];

        await Service().RequestAsync(_card.EmployeeId, Fields(city: "Ulm"), null, CancellationToken.None);

        _messages.Of<SendNotification>().Single().RecipientEmployeeIds.ShouldBe([Hr, Hr2], ignoreOrder: true);
    }

    [Fact]
    public async Task A_message_alone_is_enough_to_report_something_else()
    {
        var (error, request) = await Service().RequestAsync(_card.EmployeeId, Fields(), "Neuer Nachname nach der Heirat", CancellationToken.None);

        (error, request!.Message).ShouldBe((EmployeeError.None, "Neuer Nachname nach der Heirat"));
    }

    [Theory]
    [InlineData(null, null, null, null, null, null)]                     // nothing at all
    [InlineData("Hauptstr. 1", "86150", "Augsburg", null, null, null)]    // nothing differs from the card
    [InlineData(null, null, null, "abc", null, null)]                    // not a phone number
    [InlineData(null, null, null, null, "not-an-email", null)]
    [InlineData(null, "12345678901", null, null, null, null)]            // postal code too long
    [InlineData(null, null, null, null, null, "   ")]                    // an empty message is nothing
    public async Task A_request_must_say_something_sensible(string? street, string? postal, string? city, string? phone, string? email, string? message)
    {
        var (error, request) = await Service().RequestAsync(_card.EmployeeId, Fields(street, postal, city, phone, email), message, CancellationToken.None);

        (error, request).ShouldBe((EmployeeError.Invalid, null));
        _requests.Requests.ShouldBeEmpty();
        _messages.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_message_has_a_limit() =>
        (await Service().RequestAsync(_card.EmployeeId, Fields(), new string('x', 501), CancellationToken.None)).Error.ShouldBe(EmployeeError.Invalid);

    [Fact]
    public async Task Somebody_without_a_card_cannot_ask() =>
        (await Service().RequestAsync(Guid.NewGuid(), Fields(city: "Ulm"), null, CancellationToken.None)).Error.ShouldBe(EmployeeError.NotFound);

    [Fact]
    public async Task While_a_request_is_pending_no_second_one_is_accepted()
    {
        await Pending();

        (await Service().RequestAsync(_card.EmployeeId, Fields(city: "Neu-Ulm"), null, CancellationToken.None)).Error.ShouldBe(EmployeeError.PendingRequestExists);
        _requests.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Without_anybody_to_decide_the_request_is_refused_instead_of_vanishing()
    {
        _responsibilities.ChangeRequestHandlers.Clear();
        (await Service().RequestAsync(_card.EmployeeId, Fields(city: "Ulm"), null, CancellationToken.None)).Error.ShouldBe(EmployeeError.NoHandler);

        _responsibilities.ChangeRequestHandlers[_card.EmployeeId] = [_card.EmployeeId]; // only the person themself
        (await Service().RequestAsync(_card.EmployeeId, Fields(city: "Ulm"), null, CancellationToken.None)).Error.ShouldBe(EmployeeError.NoHandler);
        _requests.Requests.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- withdrawing

    [Fact]
    public async Task The_person_can_withdraw_a_pending_request_and_ask_again()
    {
        var request = await Pending();

        (await Service().WithdrawAsync(_card.EmployeeId, request.Id, CancellationToken.None)).ShouldBe(EmployeeError.None);

        request.Status.ShouldBe(ChangeRequestStatus.Withdrawn);
        (_card.Street, _card.City).ShouldBe(("Hauptstr. 1", "Augsburg"));
        (await Service().RequestAsync(_card.EmployeeId, Fields(city: "Ulm"), null, CancellationToken.None)).Error.ShouldBe(EmployeeError.None);
    }

    [Fact]
    public async Task A_request_of_somebody_else_looks_missing_and_a_decided_one_cannot_be_withdrawn()
    {
        var request = await Pending();

        (await Service().WithdrawAsync(Guid.NewGuid(), request.Id, CancellationToken.None)).ShouldBe(EmployeeError.NotFound);
        await Service().RejectAsync(request.Id, Hr, null, CancellationToken.None);
        (await Service().WithdrawAsync(_card.EmployeeId, request.Id, CancellationToken.None)).ShouldBe(EmployeeError.NotPending);
    }

    [Fact]
    public async Task The_person_sees_only_the_own_requests()
    {
        var request = await Pending();
        var other = new EmployeeCard { EmployeeId = Guid.NewGuid(), PersonnelNumber = "15303", FirstName = "B", LastName = "C", ReceiptName = "C", HireDate = new DateOnly(2026, 12, 1) };
        _cards.Cards[other.EmployeeId] = other;
        _responsibilities.ChangeRequestHandlers[other.EmployeeId] = [Hr];
        await Service().RequestAsync(other.EmployeeId, Fields(city: "Hof"), null, CancellationToken.None);

        (await Service().ListOwnAsync(_card.EmployeeId, CancellationToken.None)).Select(r => r.Id).ShouldBe([request.Id]);
    }

    // ---------------------------------------------------------------- HR decides

    [Fact]
    public async Task Approving_applies_only_the_given_fields_and_tells_the_person_without_data()
    {
        var request = await Pending(Fields(street: "Neue Str. 5", city: "Ulm"));
        _time.Advance(TimeSpan.FromHours(3));

        (await Service().ApproveAsync(request.Id, Hr, CancellationToken.None)).ShouldBe(EmployeeError.None);

        (_card.Street, _card.City, _card.PostalCode, _card.PrivatePhone, _card.PrivateEmail).ShouldBe(("Neue Str. 5", "Ulm", "86150", "+49 821 123456", "erika@privat.example"));
        (request.Status, request.DecidedBy, request.DecidedAt).ShouldBe((ChangeRequestStatus.Approved, (Guid?)Hr, (DateTimeOffset?)_time.GetUtcNow()));
        _card.ModifiedAt.ShouldBe(_time.GetUtcNow());
        _cards.CopiesTouched.ShouldContain(_card.EmployeeId);
        var notification = _messages.Of<SendNotification>().Single();
        notification.TemplateKey.ShouldBe(EmployeeChangeService.DecisionTemplate);
        notification.RecipientEmployeeIds.ShouldBe([_card.EmployeeId]);
        notification.Data.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rejecting_leaves_the_card_alone_keeps_the_reason_and_tells_the_person_without_data()
    {
        var request = await Pending();

        (await Service().RejectAsync(request.Id, Hr, "  Adresse bitte mit Nachweis  ", CancellationToken.None)).ShouldBe(EmployeeError.None);

        (request.Status, request.RejectionReason, request.DecidedBy).ShouldBe((ChangeRequestStatus.Rejected, "Adresse bitte mit Nachweis", (Guid?)Hr));
        (_card.Street, _card.City).ShouldBe(("Hauptstr. 1", "Augsburg"));
        _messages.Of<SendNotification>().Single().Data.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rejection_reason_has_a_limit()
    {
        var request = await Pending();

        (await Service().RejectAsync(request.Id, Hr, new string('x', 201), CancellationToken.None)).ShouldBe(EmployeeError.Invalid);
        request.Status.ShouldBe(ChangeRequestStatus.Pending);
    }

    [Fact]
    public async Task A_request_is_decided_only_once()
    {
        var request = await Pending();
        await Service().ApproveAsync(request.Id, Hr, CancellationToken.None);

        (await Service().ApproveAsync(request.Id, Hr2, CancellationToken.None)).ShouldBe(EmployeeError.NotPending);
        (await Service().RejectAsync(request.Id, Hr2, null, CancellationToken.None)).ShouldBe(EmployeeError.NotPending);
        request.DecidedBy.ShouldBe(Hr);
    }

    [Fact]
    public async Task Nobody_decides_the_own_request()
    {
        var request = await Pending();

        (await Service().ApproveAsync(request.Id, _card.EmployeeId, CancellationToken.None)).ShouldBe(EmployeeError.NotAllowed);
        (await Service().RejectAsync(request.Id, _card.EmployeeId, null, CancellationToken.None)).ShouldBe(EmployeeError.NotAllowed);
        (_card.Street, request.Status).ShouldBe(("Hauptstr. 1", ChangeRequestStatus.Pending));
    }

    [Fact]
    public async Task An_unknown_request_is_not_found()
    {
        (await Service().ApproveAsync(Guid.NewGuid(), Hr, CancellationToken.None)).ShouldBe(EmployeeError.NotFound);
        (await Service().RejectAsync(Guid.NewGuid(), Hr, null, CancellationToken.None)).ShouldBe(EmployeeError.NotFound);
    }

    [Fact]
    public async Task Pending_requests_are_listed_oldest_first()
    {
        var first = await Pending();
        _time.Advance(TimeSpan.FromMinutes(5));
        var other = new EmployeeCard { EmployeeId = Guid.NewGuid(), PersonnelNumber = "15303", FirstName = "B", LastName = "C", ReceiptName = "C", HireDate = new DateOnly(2026, 12, 1) };
        _cards.Cards[other.EmployeeId] = other;
        _responsibilities.ChangeRequestHandlers[other.EmployeeId] = [Hr];
        var second = (await Service().RequestAsync(other.EmployeeId, Fields(city: "Hof"), null, CancellationToken.None)).Request!;
        await Service().WithdrawAsync(other.EmployeeId, second.Id, CancellationToken.None);

        (await Service().PendingAsync(CancellationToken.None)).Select(r => r.Id).ShouldBe([first.Id]);
    }
}
