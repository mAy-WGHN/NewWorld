using Notifications.Contracts;

namespace Notifications.Contracts.Tests;

/// <summary>One sample per message.</summary>
internal static class Samples
{
    // In-app only, so Data may carry class 2 content; push/mail would get a generic text with the deep link.
    public static readonly SendNotification SendNotification = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        new DateTimeOffset(2028, 2, 29, 23, 59, 0, TimeSpan.FromHours(1)),
        NotificationId: Guid.Parse("0197a000-0000-7000-8000-000000000401"),
        RecipientEmployeeIds: [Guid.Parse("0197a000-0000-7000-8000-000000000002")],
        TemplateKey: "workforce.sick-note.store-manager",
        Data: new Dictionary<string, string> { ["employeeName"] = "Erika Mustermann", ["from"] = "2028-02-29" },
        Channels: [NotificationChannel.InApp],
        DeepLink: "/absences/0197a000-0000-7000-8000-000000000201");
}
