using Microsoft.AspNetCore.Diagnostics;

namespace Homestead.WebApi.Common;

public class ForbiddenAccessExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ForbiddenAccessException)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        await httpContext.Response.WriteAsJsonAsync(
            new { error = exception.Message }, cancellationToken);
        return true;
    }
}
