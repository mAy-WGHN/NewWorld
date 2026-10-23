namespace BcIntegration.Core.Errors;

/// <summary>Permanent technical BC failure (e.g. rejected credentials). <see cref="Reason"/> is a content-free code.</summary>
public sealed class BcTechnicalFailureException(string reason) : Exception($"Permanent BC failure: {reason}")
{
    public string Reason { get; } = reason;
}

/// <summary>Transient BC failure that remained after the client's retries; the message is redelivered later.</summary>
public sealed class BcTransientFailureException(string reason) : Exception($"Transient BC failure: {reason}")
{
    public string Reason { get; } = reason;
}
