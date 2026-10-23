using Notifications.Contracts;
using Notifications.Core.Templates;

namespace Notifications.Core.Tests;

internal static class Samples
{
    public static readonly Guid Max = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid Sabine = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    public static readonly Template SickNote = new(
        "workforce.sick-note.store-manager",
        Mandatory: true,
        Placeholders: new Dictionary<string, bool> { ["employeeName"] = true, ["from"] = true },
        InApp: new TextPair("Krankmeldung", "{employeeName} hat sich ab {from} krank gemeldet."),
        Push: new TextPair("Neue Mitteilung im Portal", "Öffnen, um die Mitteilung zu lesen."),
        Email: new TextPair("Neue Mitteilung im Portal", "Es liegt eine neue Mitteilung für Sie bereit."));

    public static readonly Template News = new(
        "portal.news.employee",
        Mandatory: false,
        Placeholders: new Dictionary<string, bool> { ["headline"] = false },
        InApp: new TextPair("Neuigkeit", "{headline}"),
        Push: new TextPair("Neue Mitteilung im Portal", "Öffnen, um die Mitteilung zu lesen."),
        Email: new TextPair("Neue Mitteilung im Portal", "Es liegt eine neue Mitteilung für Sie bereit."));

    public static TemplateCatalog Catalog() => new([SickNote, News]);

    public static SendNotification Command(
        string templateKey = "workforce.sick-note.store-manager",
        IReadOnlyList<NotificationChannel>? channels = null,
        IReadOnlyDictionary<string, string>? data = null,
        IReadOnlyList<Guid>? recipients = null,
        Guid? notificationId = null,
        string? deepLink = "/absences/0197a000-0000-7000-8000-000000000201") => new(
        Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero),
        notificationId ?? Guid.NewGuid(),
        recipients ?? [Max],
        templateKey,
        data ?? new Dictionary<string, string> { ["employeeName"] = "Erika Mustermann", ["from"] = "2026-03-30" },
        channels ?? [NotificationChannel.InApp],
        deepLink);
}
