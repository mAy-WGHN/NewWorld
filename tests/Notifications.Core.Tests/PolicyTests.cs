using Notifications.Contracts;
using Notifications.Core.Delivery;
using static Notifications.Contracts.NotificationChannel;

namespace Notifications.Core.Tests;

/// <summary>The protection rule of the plan: class 2 data only for in-app; everything else is rejected before anything is stored or sent.</summary>
public class PolicyTests
{
    private static readonly Templates.TemplateCatalog Catalog = Samples.Catalog();

    private static Rejection? Check(Contracts.SendNotification command) => NotificationPolicy.Check(command, Catalog).Rejection;

    [Fact]
    public void Valid_in_app_command_with_sensitive_data_is_accepted()
    {
        var result = NotificationPolicy.Check(Samples.Command(), Catalog);

        result.Rejection.ShouldBeNull();
        result.Template!.Key.ShouldBe("workforce.sick-note.store-manager");
    }

    [Theory]
    [InlineData(new[] { Push })]
    [InlineData(new[] { Email })]
    [InlineData(new[] { InApp, Push })]
    [InlineData(new[] { InApp, Email, Push })]
    public void Sensitive_data_with_any_channel_besides_in_app_is_rejected(NotificationChannel[] channels) =>
        Check(Samples.Command(channels: channels)).ShouldBe(Rejection.SensitiveDataOnNonInAppChannel);

    [Fact]
    public void Non_sensitive_data_may_use_all_channels() =>
        Check(Samples.Command("portal.news.employee", [InApp, Push, Email], new Dictionary<string, string> { ["headline"] = "Neue Preise" })).ShouldBeNull();

    [Fact]
    public void Data_is_only_required_for_the_in_app_text()
    {
        var onlyPush = Samples.Command(channels: [Push], data: new Dictionary<string, string>());

        // Push text is generic: nothing to fill in. Requiring data here would only invite senders to send sensitive values.
        NotificationPolicy.Check(onlyPush, Catalog).Rejection.ShouldBeNull();
    }

    [Fact]
    public void Empty_sensitive_value_does_not_count_as_data() =>
        Check(Samples.Command(channels: [Push], data: new Dictionary<string, string> { ["employeeName"] = "  " })).ShouldBeNull();

    [Fact]
    public void Unknown_template_is_rejected() =>
        Check(Samples.Command("workforce.unknown.store-manager")).ShouldBe(Rejection.UnknownTemplate);

    [Fact]
    public void Undeclared_data_keys_are_rejected_because_their_class_is_unknown() =>
        Check(Samples.Command("portal.news.employee", [Push], new Dictionary<string, string> { ["headline"] = "x", ["diagnosis"] = "y" })).ShouldBe(Rejection.UnknownDataKey);

    [Fact]
    public void Missing_value_for_a_placeholder_of_the_in_app_text_is_rejected() =>
        Check(Samples.Command(data: new Dictionary<string, string> { ["employeeName"] = "Erika" })).ShouldBe(Rejection.MissingData);

    [Fact]
    public void Overlong_values_are_rejected() =>
        Check(Samples.Command(data: new Dictionary<string, string> { ["employeeName"] = new string('x', 501), ["from"] = "2026-03-30" })).ShouldBe(Rejection.DataTooLong);

    [Fact]
    public void No_recipients_is_rejected() => Check(Samples.Command(recipients: [])).ShouldBe(Rejection.NoRecipients);

    [Fact]
    public void Too_many_recipients_are_rejected() =>
        Check(Samples.Command(recipients: Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToList())).ShouldBe(Rejection.TooManyRecipients);

    [Fact]
    public void Empty_recipient_id_is_rejected() => Check(Samples.Command(recipients: [Guid.Empty])).ShouldBe(Rejection.NoRecipients);

    [Theory]
    [InlineData(new NotificationChannel[0])]
    [InlineData(new[] { Unknown })]
    [InlineData(new[] { InApp, Unknown })]
    public void Missing_or_unknown_channels_are_rejected(NotificationChannel[] channels) =>
        Check(Samples.Command(channels: channels)).ShouldBe(Rejection.InvalidChannels);

    [Theory]
    [InlineData("https://evil.example/login")]
    [InlineData("//evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("absences/1")]
    [InlineData("/a b")]
    public void Deep_links_must_be_relative_portal_paths(string link) =>
        Check(Samples.Command(deepLink: link)).ShouldBe(Rejection.InvalidDeepLink);

    [Fact]
    public void Deep_link_is_optional_for_in_app() => Check(Samples.Command(deepLink: null)).ShouldBeNull();

    [Fact]
    public void Rejection_codes_contain_no_content()
    {
        foreach (var rejection in Enum.GetValues<Rejection>())
        {
            rejection.ToString().ShouldNotContain("Erika");
        }
    }
}
