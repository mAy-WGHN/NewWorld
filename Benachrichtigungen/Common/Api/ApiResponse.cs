namespace Benachrichtigungen.Common.Api;

public record SuccessResponse<T>(T Data)
{
    public bool Success => true;
}

public record ErrorResponse(string Message, IReadOnlyList<ApiError> Errors)
{
    public bool Success => false;
}
