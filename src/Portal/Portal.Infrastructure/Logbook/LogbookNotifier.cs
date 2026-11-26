using System.Security.Cryptography;
using System.Text;
using Notifications.Contracts;
using Portal.Core;
using Portal.Core.Logbook;
using Shared.Authorization;
using Shared.Infrastructure.Messaging;

namespace Portal.Infrastructure.Logbook;

/// <summary>
/// The notifications of the Berichtsheft (decision 49), sent as <c>SendNotification</c> through the outbox in the same transaction as the change. They carry
/// <b>nothing from the report</b> (class 2): InApp and a generic push, the template says only that something is waiting, the link leads to the page where the
/// content is loaded. The notification id is derived from what happened, so a redelivery is not shown twice. The person who did it is not notified.
/// </summary>
public sealed class LogbookNotifier(Outbox<PortalDbContext> outbox, IPermissionCache permissions, TimeProvider time)
{
    public const string Submitted = "portal.logbook-submitted.regional";
    public const string SubmittedDistrict = "portal.logbook-submitted.district";
    public const string ReplyToStore ="portal.logbook-reply.store";
    public const string ReplyToRegional = "portal.logbook-reply.regional";
    public const string FollowedUp = "portal.logbook-followed-up.regional";
    public const string Escalated = "portal.logbook-escalated.district";
    public const string Reminder = "portal.logbook-reminder.store";

    /// <summary>The store managers of a store: those who hold the write privilege for exactly this store (not everybody with it everywhere, such as the administration).</summary>
    public IReadOnlyList<Guid> StoreManagersOf(Guid storeId) =>
        [.. permissions.All()
            .Where(s => s.Privileges.TryGetValue(PortalPrivileges.LogbookWrite, out var reach) && !reach.Unrestricted && reach.StoreIds.Contains(storeId))
            .Select(s => s.EmployeeId)
            .Order()];

    /// <summary>A handed-in report: the regional leaders, or the district leaders if the store has no regional leader.</summary>
    public void ReportSubmitted(LogbookReportRecord report, Guid actor, LogbookRecipients recipients)
    {
        Send(Submitted, $"{report.Id}:submitted", recipients.Primary, actor, report.Id, $"/logbook/reports/{report.Id}");

        // The district leaders always know about it, but quietly: an info in the portal only, no push (those who were told above as the primary
        // recipients, because the store has no regional leader, are not told twice).
        Send(SubmittedDistrict, $"{report.Id}:submitted", recipients.District.Except(recipients.Primary), actor, report.Id, $"/logbook/reports/{report.Id}", quiet: true);
    }

    /// <summary>A reply: a regional leader answers the store managers, a district leader answers the regional leaders (nobody if the store has none).</summary>
    public void Replied(LogbookReportRecord report, LogbookCommentRecord comment, LogbookRecipients recipients)
    {
        if (comment.Kind != LogbookCommentKind.Reply)
        {
            return; // internal comments tell nobody
        }

        if (comment.Level == LogbookLevel.Regional)
        {
            Send(ReplyToStore, $"{comment.Id}:reply", StoreManagersOf(report.StoreId), comment.AuthorId, report.Id, $"/logbook/reports/{report.Id}");
        }
        else
        {
            Send(ReplyToRegional, $"{comment.Id}:reply", recipients.Regional, comment.AuthorId, report.Id, $"/logbook/reports/{report.Id}");
        }
    }

    public void ReportFollowedUp(LogbookReportRecord report, Guid actor, LogbookRecipients recipients) =>
        Send(FollowedUp, $"{report.Id}:followed-up:{report.FollowedUpAt?.UtcTicks}", recipients.Primary, actor, report.Id, $"/logbook/reports/{report.Id}");

    public void ReportEscalated(LogbookReportRecord report, Guid actor, LogbookRecipients recipients) =>
        Send(Escalated, $"{report.Id}:escalated", recipients.District, actor, report.Id, $"/logbook/reports/{report.Id}");

    /// <summary>A reminder to the store managers of a store that has not handed in the report of the week; <paramref name="key"/> makes it unique (the reminder row, or store, week and point).</summary>
    public bool ReminderSent(Guid storeId, string key, Guid? actor, Guid correlation) =>
        Send(Reminder, key, StoreManagersOf(storeId), actor ?? Guid.Empty, correlation, "/logbook");

    private bool Send(string template, string key, IEnumerable<Guid> recipients, Guid actor, Guid correlation, string deepLink, bool quiet = false)
    {
        var to = recipients.Where(r => r != actor).Distinct().ToList();
        if (to.Count == 0)
        {
            return false;
        }

        var now = time.GetUtcNow();
        outbox.Send(new SendNotification(
            Guid.CreateVersion7(now), correlation, now, Deterministic($"{template}:{key}"), to, template, new Dictionary<string, string>(),
            quiet ? [NotificationChannel.InApp] : [NotificationChannel.InApp, NotificationChannel.Push], deepLink));
        return true;
    }

    /// <summary>The same text always gives the same id (idempotence of the notification).</summary>
    private static Guid Deterministic(string text) => new(SHA256.HashData(Encoding.UTF8.GetBytes(text))[..16]);
}
