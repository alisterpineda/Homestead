using Microsoft.AspNetCore.Diagnostics;

namespace Homestead.WebApi.Common;

public abstract class DomainExceptionHandler<TException>(int statusCode) : IExceptionHandler
    where TException : Exception
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not TException)
            return false;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new { error = exception.Message }, cancellationToken);
        return true;
    }
}
