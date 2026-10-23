namespace BcIntegration.Core.Messaging;

internal static class MessageIds
{
    /// <summary>Time-ordered ID (UUIDv7) from the injected clock.</summary>
    public static Guid New(TimeProvider time) => Guid.CreateVersion7(time.GetUtcNow());
}
