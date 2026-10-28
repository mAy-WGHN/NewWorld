namespace Notifications.Api;

public sealed record NotificationDto(Guid Id, string TemplateKey, string Title, string Body, string? DeepLink, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record UnreadCountDto(int Count);

public sealed record ChannelPreferenceDto(string Channel, bool Enabled);

/// <summary>A notification type with the person's choice per channel; mandatory types cannot be switched off.</summary>
public sealed record NotificationTypeDto(string TemplateKey, bool Mandatory, IReadOnlyList<ChannelPreferenceDto> Channels);

public sealed record SetPreferenceRequest(bool Enabled);

public sealed record TextDto(string Title, string Body);

/// <summary>The texts of a template; a text that is <c>null</c> is the one of the file (default).</summary>
public sealed record TemplateTextsDto(TextDto? InApp, TextDto? Push, TextDto? Email);

/// <summary>A variable a text may use: <c>Class</c> 1 (everywhere) or 2 (in-app, and the mail text where the template allows it); <c>Source</c> is <c>Recipient</c> (filled in by Notifications) or <c>Sender</c>.</summary>
public sealed record VariableDto(string Name, int Class, string Source);

/// <summary>
/// A template for the maintenance. <c>SensitiveText</c>: the text itself is class 2, push text and mail subject are fixed (the mail text too unless
/// <c>MailMayContainSensitive</c>). Texts that cannot be changed keep the file's text.
/// </summary>
public sealed record TemplateDto(
    string Key, bool Mandatory, bool SensitiveText, bool MailMayContainSensitive, bool Customized, TemplateTextsDto Default, TemplateTextsDto Effective,
    IReadOnlyList<VariableDto> Variables, DateTimeOffset? ChangedAt, Guid? ChangedBy);

public sealed record SaveTemplateRequest(TextDto? InApp, TextDto? Push, TextDto? Email);

public sealed record PreviewTemplateRequest(TextDto? InApp, TextDto? Push, TextDto? Email, Dictionary<string, string>? Sample);
