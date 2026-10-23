namespace Shared.Infrastructure.Messaging;

/// <summary>
/// A technical failure that redelivery cannot fix (e.g. rejected credentials). The message goes to the dead-letter queue
/// with <see cref="ReasonCode"/>, which must not contain message content or personal data.
/// </summary>
public sealed class PermanentMessageFailureException(string reasonCode)
    : Exception($"Permanent message failure: {reasonCode}")
{
    public string ReasonCode { get; } = reasonCode;
}
