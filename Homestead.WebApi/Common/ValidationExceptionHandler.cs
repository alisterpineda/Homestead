using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;

namespace Homestead.WebApi.Common;

public class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validationException)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        var errors = validationException.Errors
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());

        await httpContext.Response.WriteAsJsonAsync(new { errors }, cancellationToken);

        return true;
    }
}
