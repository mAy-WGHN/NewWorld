namespace Notifications.Api;

public sealed record NotificationDto(Guid Id, string TemplateKey, string Title, string Body, string? DeepLink, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record UnreadCountDto(int Count);

public sealed record ChannelPreferenceDto(string Channel, bool Enabled);

/// <summary>A notification type with the person's choice per channel; mandatory types cannot be switched off.</summary>
public sealed record NotificationTypeDto(string TemplateKey, bool Mandatory, IReadOnlyList<ChannelPreferenceDto> Channels);

public sealed record SetPreferenceRequest(bool Enabled);
