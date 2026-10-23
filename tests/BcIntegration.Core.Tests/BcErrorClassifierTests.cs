using BcIntegration.Contracts;
using BcIntegration.Core.Errors;

namespace BcIntegration.Core.Tests;

public class BcErrorClassifierTests
{
    [Theory]
    [InlineData(429)]
    [InlineData(408)]
    [InlineData(409)]
    [InlineData(412)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void Throttling_concurrency_and_server_errors_are_transient(int status) =>
        BcErrorClassifier.Classify(status, bcErrorCode: null).Kind.ShouldBe(BcErrorKind.Transient);

    [Fact]
    public void Timeout_is_transient() => BcErrorClassifier.ClassifyTimeout().Kind.ShouldBe(BcErrorKind.Transient);

    [Fact]
    public void Validation_error_is_a_permanent_business_failure()
    {
        var error = BcErrorClassifier.Classify(400, "Internal_ValidationError");

        error.Kind.ShouldBe(BcErrorKind.Business);
        error.BusinessCode.ShouldBe(BcSyncErrorCode.ValidationFailed);
    }

    [Fact]
    public void Missing_referenced_record_is_a_permanent_business_failure()
    {
        var error = BcErrorClassifier.Classify(400, "Internal_RecordNotFound");

        error.Kind.ShouldBe(BcErrorKind.Business);
        error.BusinessCode.ShouldBe(BcSyncErrorCode.ReferencedRecordMissing);
    }

    [Theory]
    [InlineData(401, "BcUnauthorized")]
    [InlineData(403, "BcForbidden")]
    [InlineData(404, "BcEndpointNotFound")]
    [InlineData(405, "BcClientError405")]
    public void Configuration_problems_are_permanent_technical_failures(int status, string reason)
    {
        var error = BcErrorClassifier.Classify(status, null);

        error.Kind.ShouldBe(BcErrorKind.Technical);
        error.TechnicalReason.ShouldBe(reason);
    }
}
