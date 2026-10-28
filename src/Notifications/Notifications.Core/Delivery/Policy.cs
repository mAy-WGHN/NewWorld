using System.Text.RegularExpressions;
using Notifications.Contracts;
using Notifications.Core.Templates;

namespace Notifications.Core.Delivery;

/// <summary>Why a command was refused. Codes only: they go to the log and the dead-letter queue and must not carry content.</summary>
public enum Rejection
{
    Unknown = 0,
    UnknownTemplate = 1,
    NoRecipients = 2,
    TooManyRecipients = 3,
    InvalidChannels = 4,
    InvalidDeepLink = 5,
    UnknownDataKey = 6,
    MissingData = 7,
    DataTooLong = 8,

    /// <summary>The protection rule: class 2 data never with push, by mail only for templates that allow it, otherwise in-app only.</summary>
    SensitiveDataOnNonInAppChannel = 9,
}

public sealed record PolicyResult(Template? Template, Rejection? Rejection);

public sealed class NotificationRejectedException(Rejection reason) : Exception($"Notification rejected: {reason}")
{
    public Rejection Reason { get; } = reason;
}

/// <summary>
/// Checks a <see cref="SendNotification"/> before anything is stored or sent. The rule of the plan: data the template marks as class 2
/// is only accepted for in-app delivery, and by mail only if the template allows it; never with push. Data keys the template does not declare are refused:
/// their class is unknown and the stricter class applies.
/// </summary>
public static partial class NotificationPolicy
{
    public const int MaxRecipients = 1000;
    public const int MaxValueLength = 500;

    [GeneratedRegex("^/[A-Za-z0-9._~!$&'()*+,;=:@%/?#\\-\\[\\]]*$")]
    private static partial Regex RelativePath();

    public static PolicyResult Check(SendNotification command, TemplateCatalog catalog) =>
        catalog.TryGet(command.TemplateKey, out var template) ? Check(command, template!) : Reject(Rejection.UnknownTemplate);

    /// <summary>Checks the command against the template in force (the file, with the texts HR changed).</summary>
    public static PolicyResult Check(SendNotification command, Template template)
    {
        if (command.RecipientEmployeeIds.Count == 0 || command.RecipientEmployeeIds.Any(id => id == Guid.Empty))
        {
            return Reject(Rejection.NoRecipients);
        }

        if (command.RecipientEmployeeIds.Count > MaxRecipients)
        {
            return Reject(Rejection.TooManyRecipients);
        }

        if (command.Channels.Count == 0 || command.Channels.Any(c => c == NotificationChannel.Unknown || !Enum.IsDefined(c)))
        {
            return Reject(Rejection.InvalidChannels);
        }

        if (command.DeepLink is { } link && (link.StartsWith("//", StringComparison.Ordinal) || !RelativePath().IsMatch(link)))
        {
            return Reject(Rejection.InvalidDeepLink);
        }

        if (command.Data.Keys.Any(key => !template.Placeholders.ContainsKey(key)))
        {
            return Reject(Rejection.UnknownDataKey);
        }

        if (command.Data.Values.Any(value => value.Length > MaxValueLength))
        {
            return Reject(Rejection.DataTooLong);
        }

        // Class 2 data never goes to push; by mail only if the template allows it (recipients such as HR).
        var sensitive = command.Data.Any(d => template.Placeholders[d.Key] && !string.IsNullOrWhiteSpace(d.Value));
        if (sensitive && (command.Channels.Contains(NotificationChannel.Push) || (command.Channels.Contains(NotificationChannel.Email) && !template.MailMayContainSensitive)))
        {
            return Reject(Rejection.SensitiveDataOnNonInAppChannel);
        }

        if (command.Channels.Distinct().Any(c => template.RequiredFor(c).Any(p => !command.Data.TryGetValue(p, out var value) || string.IsNullOrWhiteSpace(value))))
        {
            return Reject(Rejection.MissingData);
        }

        return new PolicyResult(template, null);
    }

    private static PolicyResult Reject(Rejection reason) => new(null, reason);
}
