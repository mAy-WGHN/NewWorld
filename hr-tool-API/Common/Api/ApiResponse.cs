namespace HrTool_API.Common.Api;

public record PageMeta(int Total, int Offset, int? Limit);

public record PagedResult<T>(IReadOnlyList<T> Items, PageMeta Meta);

public record SuccessResponse<T>(T Data)
{
    public bool Success => true;
}

public record ErrorResponse(string Message, IReadOnlyList<ApiError> Errors)
{
    public bool Success => false;
}
