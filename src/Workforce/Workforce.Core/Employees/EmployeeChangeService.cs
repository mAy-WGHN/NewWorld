using System.Text.RegularExpressions;
using Notifications.Contracts;
using Workforce.Core.Absences;

namespace Workforce.Core.Employees;

public enum ChangeRequestStatus
{
    Unknown = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Withdrawn = 4,
}

/// <summary>
/// A person's request to change their private contact data (or a message about something else). The person never changes the data
/// themself, not even the own address: they ask, HR decides. Fields that are <c>null</c> stay as they are.
/// </summary>
public sealed class EmployeeChangeRequest
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? PrivatePhone { get; set; }
    public string? PrivateEmail { get; set; }

    /// <summary>Free text for something the fields do not cover (e.g. a new name); no health information.</summary>
    public string? Message { get; set; }

    public ChangeRequestStatus Status { get; set; } = ChangeRequestStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public Guid? DecidedBy { get; set; }

    public string? RejectionReason { get; set; }
}

/// <summary>The contact data a person can ask to change; <c>null</c> = no change.</summary>
public sealed record ChangeRequestFields(string? Street, string? PostalCode, string? City, string? PrivatePhone, string? PrivateEmail);

/// <summary>Change requests of the employee card.</summary>
public interface IEmployeeChangeStore
{
    void Add(EmployeeChangeRequest request);

    Task<EmployeeChangeRequest?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The requests of a person, newest first.</summary>
    Task<IReadOnlyList<EmployeeChangeRequest>> ForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    /// <summary>All pending requests, oldest first.</summary>
    Task<IReadOnlyList<EmployeeChangeRequest>> PendingAsync(CancellationToken cancellationToken);
}

/// <summary>Validation shared by the card and the change requests.</summary>
public static partial class EmployeeValidation
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^[0-9 +\-/().]+$")]
    private static partial Regex PhonePattern();

    public static bool IsEmail(string value) => EmailPattern().IsMatch(value);

    public static bool IsPhone(string value) => PhonePattern().IsMatch(value) && value.Count(char.IsDigit) >= 5 && value.Length <= 30;
}

/// <summary>
/// Change requests for the private contact data: the person asks, HR is told (without any of the data in the message) and decides.
/// Free of infrastructure; callers check authorization first and save the unit of work afterwards.
/// </summary>
public sealed class EmployeeChangeService(
    IEmployeeChangeStore requests,
    IEmployeeCardStore cards,
    IResponsibilityDirectory responsibilities,
    IWorkforceMessages messages,
    TimeProvider time)
{
    public const string RequestTemplate = "workforce.employee-change-request.hr";
    public const string DecisionTemplate = "workforce.employee-change-decision.employee";
    private const int MaxMessage = 500;
    private const int MaxReason = 200;

    /// <summary>The person asks for a change. At least one field must differ from the card, or a message must say what is meant.</summary>
    public async Task<(EmployeeError Error, EmployeeChangeRequest? Request)> RequestAsync(
        Guid employeeId, ChangeRequestFields fields, string? message, CancellationToken cancellationToken)
    {
        if (await cards.GetAsync(employeeId, cancellationToken) is not { } card)
        {
            return (EmployeeError.NotFound, null);
        }

        var text = Clean(message);
        var changes = new ChangeRequestFields(
            Differs(fields.Street, card.Street), Differs(fields.PostalCode, card.PostalCode), Differs(fields.City, card.City),
            Differs(fields.PrivatePhone, card.PrivatePhone), Differs(fields.PrivateEmail, card.PrivateEmail));
        var nothing = changes is { Street: null, PostalCode: null, City: null, PrivatePhone: null, PrivateEmail: null };
        if ((nothing && text is null) || (text?.Length ?? 0) > MaxMessage || !FieldsAreSensible(changes))
        {
            return (EmployeeError.Invalid, null);
        }

        if ((await requests.ForEmployeeAsync(employeeId, cancellationToken)).Any(r => r.Status == ChangeRequestStatus.Pending))
        {
            return (EmployeeError.PendingRequestExists, null);
        }

        var handlers = (await responsibilities.ChangeRequestHandlersAsync(employeeId, cancellationToken)).Where(h => h != employeeId).Distinct().ToList();
        if (handlers.Count == 0)
        {
            return (EmployeeError.NoHandler, null);
        }

        var now = time.GetUtcNow();
        var request = new EmployeeChangeRequest
        {
            Id = Guid.CreateVersion7(now), EmployeeId = employeeId, Street = changes.Street, PostalCode = changes.PostalCode, City = changes.City,
            PrivatePhone = changes.PrivatePhone, PrivateEmail = changes.PrivateEmail, Message = text, CreatedAt = now,
        };
        requests.Add(request);
        Notify(RequestTemplate, handlers, request, $"/employee-change-requests/{request.Id}", now);
        return (EmployeeError.None, request);
    }

    public async Task<EmployeeError> WithdrawAsync(Guid employeeId, Guid requestId, CancellationToken cancellationToken)
    {
        if (await requests.GetAsync(requestId, cancellationToken) is not { } request || request.EmployeeId != employeeId)
        {
            return EmployeeError.NotFound;
        }

        if (request.Status != ChangeRequestStatus.Pending)
        {
            return EmployeeError.NotPending;
        }

        request.Status = ChangeRequestStatus.Withdrawn;
        return EmployeeError.None;
    }

    public async Task<IReadOnlyList<EmployeeChangeRequest>> ListOwnAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await requests.ForEmployeeAsync(employeeId, cancellationToken);

    public async Task<IReadOnlyList<EmployeeChangeRequest>> PendingAsync(CancellationToken cancellationToken) => await requests.PendingAsync(cancellationToken);

    public async Task<EmployeeChangeRequest?> GetAsync(Guid requestId, CancellationToken cancellationToken) => await requests.GetAsync(requestId, cancellationToken);

    /// <summary>HR approves: the given fields are applied to the card. Nobody decides their own request.</summary>
    public async Task<EmployeeError> ApproveAsync(Guid requestId, Guid actorId, CancellationToken cancellationToken)
    {
        var (error, request) = await PendingFor(requestId, actorId, cancellationToken);
        if (request is null)
        {
            return error;
        }

        if (await cards.GetAsync(request.EmployeeId, cancellationToken) is not { } card)
        {
            return EmployeeError.NotFound;
        }

        var now = time.GetUtcNow();
        card.Street = request.Street ?? card.Street;
        card.PostalCode = request.PostalCode ?? card.PostalCode;
        card.City = request.City ?? card.City;
        card.PrivatePhone = request.PrivatePhone ?? card.PrivatePhone;
        card.PrivateEmail = request.PrivateEmail ?? card.PrivateEmail;
        card.ModifiedAt = now;
        await cards.CardChangedAsync(card, cancellationToken);
        Decide(request, ChangeRequestStatus.Approved, actorId, null, now);
        return EmployeeError.None;
    }

    public async Task<EmployeeError> RejectAsync(Guid requestId, Guid actorId, string? reason, CancellationToken cancellationToken)
    {
        var (error, request) = await PendingFor(requestId, actorId, cancellationToken);
        if (request is null)
        {
            return error;
        }

        var text = Clean(reason);
        if ((text?.Length ?? 0) > MaxReason)
        {
            return EmployeeError.Invalid;
        }

        Decide(request, ChangeRequestStatus.Rejected, actorId, text, time.GetUtcNow());
        return EmployeeError.None;
    }

    private async Task<(EmployeeError Error, EmployeeChangeRequest? Request)> PendingFor(Guid requestId, Guid actorId, CancellationToken cancellationToken)
    {
        if (await requests.GetAsync(requestId, cancellationToken) is not { } request)
        {
            return (EmployeeError.NotFound, null);
        }

        if (request.EmployeeId == actorId)
        {
            return (EmployeeError.NotAllowed, null);
        }

        return request.Status == ChangeRequestStatus.Pending ? (EmployeeError.None, request) : (EmployeeError.NotPending, null);
    }

    private void Decide(EmployeeChangeRequest request, ChangeRequestStatus status, Guid actorId, string? reason, DateTimeOffset now)
    {
        request.Status = status;
        request.DecidedAt = now;
        request.DecidedBy = actorId;
        request.RejectionReason = reason;
        Notify(DecisionTemplate, [request.EmployeeId], request, "/me/employee-card", now);
    }

    /// <summary>Notifications never carry the data of the request (class 2); they only say that something is waiting.</summary>
    private void Notify(string templateKey, IReadOnlyList<Guid> recipients, EmployeeChangeRequest request, string deepLink, DateTimeOffset now) =>
        messages.Send(new SendNotification(
            Guid.CreateVersion7(now), request.Id, now, DeterministicGuid.From($"{request.Id}:{templateKey}"),
            recipients, templateKey, new Dictionary<string, string>(), [NotificationChannel.InApp, NotificationChannel.Push], deepLink));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The cleaned value if it differs from the current one, otherwise <c>null</c> (nothing to change).</summary>
    private static string? Differs(string? requested, string? current) =>
        Clean(requested) is { } value && !string.Equals(value, current, StringComparison.Ordinal) ? value : null;

    private static bool FieldsAreSensible(ChangeRequestFields f) =>
        (f.Street?.Length ?? 0) <= 200 && (f.PostalCode?.Length ?? 0) <= 10 && (f.City?.Length ?? 0) <= 100
        && (f.PrivateEmail is not { } email || (email.Length <= 200 && EmployeeValidation.IsEmail(email)))
        && (f.PrivatePhone is not { } phone || EmployeeValidation.IsPhone(phone));
}
