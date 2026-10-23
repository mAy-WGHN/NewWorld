using BcIntegration.Contracts;

namespace BcIntegration.Core.Errors;

public enum BcErrorKind
{
    Unknown = 0,

    /// <summary>Retry (throttling, server errors, timeouts, concurrency conflicts).</summary>
    Transient = 1,

    /// <summary>BC rejected the data: report <c>BcSyncFailed</c>, no retry.</summary>
    Business = 2,

    /// <summary>Configuration or programming problem: dead letter, no retry.</summary>
    Technical = 3,
}

/// <summary>Classified BC error. Codes only, never content from BC.</summary>
public sealed record BcError(BcErrorKind Kind, BcSyncErrorCode? BusinessCode = null, string? TechnicalReason = null);

/// <summary>
/// Maps a BC HTTP answer to an error class. TODO(BC-Modell): BC error codes are assumed from the API v2.0 error format
/// (<c>{"error":{"code":...}}</c>) and must be verified against the real BC.
/// </summary>
public static class BcErrorClassifier
{
    public static BcError ClassifyTimeout() => new(BcErrorKind.Transient);

    public static BcError Classify(int statusCode, string? bcErrorCode) => statusCode switch
    {
        408 or 429 or >= 500 => new BcError(BcErrorKind.Transient),

        // Optimistic concurrency (ETag) lost: re-read and write again.
        409 or 412 => new BcError(BcErrorKind.Transient),

        400 when bcErrorCode?.Contains("RecordNotFound", StringComparison.OrdinalIgnoreCase) == true
            => new BcError(BcErrorKind.Business, BcSyncErrorCode.ReferencedRecordMissing),
        400 or 422 => new BcError(BcErrorKind.Business, BcSyncErrorCode.ValidationFailed),

        401 => new BcError(BcErrorKind.Technical, TechnicalReason: "BcUnauthorized"),
        403 => new BcError(BcErrorKind.Technical, TechnicalReason: "BcForbidden"),
        404 => new BcError(BcErrorKind.Technical, TechnicalReason: "BcEndpointNotFound"),
        _ => new BcError(BcErrorKind.Technical, TechnicalReason: $"BcClientError{statusCode}"),
    };
}
