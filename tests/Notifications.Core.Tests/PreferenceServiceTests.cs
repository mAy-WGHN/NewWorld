using Notifications.Core.Delivery;
using static Notifications.Contracts.NotificationChannel;
using static Notifications.Core.Tests.Samples;

namespace Notifications.Core.Tests;

public class PreferenceServiceTests
{
    private readonly FakePreferenceWriter _writer = new();

    private PreferenceService Service() => new(Catalog(), _writer);

    [Fact]
    public async Task Optional_notification_types_can_be_switched_off_and_on_per_channel()
    {
        (await Service().SetAsync(Max, "portal.news.employee", Push, false, CancellationToken.None)).ShouldBe(PreferenceResult.Saved);
        (await Service().SetAsync(Max, "portal.news.employee", Push, true, CancellationToken.None)).ShouldBe(PreferenceResult.Saved);

        _writer.Rows.ShouldHaveSingleItem().ShouldBe(new Preference(Max, "portal.news.employee", Push, true));
    }

    [Fact]
    public async Task Mandatory_types_cannot_be_switched_off_on_any_channel()
    {
        (await Service().SetAsync(Max, "workforce.sick-note.store-manager", Push, false, CancellationToken.None)).ShouldBe(PreferenceResult.Mandatory);
        (await Service().SetAsync(Max, "workforce.sick-note.store-manager", InApp, false, CancellationToken.None)).ShouldBe(PreferenceResult.Mandatory);

        _writer.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Switching_a_mandatory_type_on_is_harmless_but_still_not_stored()
    {
        (await Service().SetAsync(Max, "workforce.sick-note.store-manager", Push, true, CancellationToken.None)).ShouldBe(PreferenceResult.Mandatory);
    }

    [Fact]
    public async Task Unknown_types_and_channels_are_refused()
    {
        (await Service().SetAsync(Max, "portal.unknown.employee", Push, false, CancellationToken.None)).ShouldBe(PreferenceResult.UnknownType);
        (await Service().SetAsync(Max, "portal.news.employee", Unknown, false, CancellationToken.None)).ShouldBe(PreferenceResult.InvalidChannel);
        (await Service().SetAsync(Guid.Empty, "portal.news.employee", Push, false, CancellationToken.None)).ShouldBe(PreferenceResult.UnknownType);
    }
}

internal sealed class FakePreferenceWriter : IPreferenceWriter
{
    public List<Preference> Rows { get; } = [];

    public Task SaveAsync(Preference preference, CancellationToken cancellationToken)
    {
        Rows.RemoveAll(r => r.EmployeeId == preference.EmployeeId && r.TemplateKey == preference.TemplateKey && r.Channel == preference.Channel);
        Rows.Add(preference);
        return Task.CompletedTask;
    }
}
