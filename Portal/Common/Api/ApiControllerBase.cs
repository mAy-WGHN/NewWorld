using Microsoft.AspNetCore.Mvc;

namespace Portal.Common.Api;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult Success<T>(T data) => Ok(new SuccessResponse<T>(data));

    protected IActionResult SuccessEmpty() => Ok(new SuccessResponse<object?>(null));

    protected IActionResult CreatedData<T>(T data) =>
        StatusCode(StatusCodes.Status201Created, new SuccessResponse<T>(data));

    protected IActionResult CreatedEmpty() =>
        StatusCode(StatusCodes.Status201Created, new SuccessResponse<object?>(null));
}
