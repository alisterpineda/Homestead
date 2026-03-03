using Microsoft.AspNetCore.Diagnostics;

namespace Homestead.WebApi.Common;

public class BadRequestExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadRequestException badRequest)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        if (badRequest.Errors is not null)
            await httpContext.Response.WriteAsJsonAsync(
                new { errors = badRequest.Errors }, cancellationToken);
        else
            await httpContext.Response.WriteAsJsonAsync(
                new { error = badRequest.Message }, cancellationToken);

        return true;
    }
}
