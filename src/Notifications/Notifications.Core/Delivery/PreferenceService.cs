using Notifications.Contracts;
using Notifications.Core.Templates;

namespace Notifications.Core.Delivery;

public enum PreferenceResult
{
    Unknown = 0,
    Saved = 1,
    UnknownType = 2,
    InvalidChannel = 3,

    /// <summary>Mandatory notifications (e.g. a sick note for the store manager) cannot be switched off.</summary>
    Mandatory = 4,
}

public interface IPreferenceWriter
{
    Task SaveAsync(Preference preference, CancellationToken cancellationToken);
}

/// <summary>A person's own choice per notification type and channel. Mandatory types are not stored at all: they always apply.</summary>
public sealed class PreferenceService(TemplateCatalog catalog, IPreferenceWriter writer)
{
    public async Task<PreferenceResult> SetAsync(Guid employeeId, string templateKey, NotificationChannel channel, bool enabled, CancellationToken cancellationToken)
    {
        if (employeeId == Guid.Empty || !catalog.TryGet(templateKey, out var template))
        {
            return PreferenceResult.UnknownType;
        }

        if (channel == NotificationChannel.Unknown || !Enum.IsDefined(channel))
        {
            return PreferenceResult.InvalidChannel;
        }

        if (template!.Mandatory)
        {
            return PreferenceResult.Mandatory;
        }

        await writer.SaveAsync(new Preference(employeeId, templateKey, channel, enabled), cancellationToken);
        return PreferenceResult.Saved;
    }
}
