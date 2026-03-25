using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Common;

public class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problemDetails = exception switch
        {
            AccessDeniedException e =>
                ProblemDetailsFactory.Forbidden(e.Message),
            EntityNotFoundException e =>
                ProblemDetailsFactory.Create(StatusCodes.Status404NotFound, "Not Found", e.Message),
            DuplicateEntityException e =>
                ProblemDetailsFactory.Create(StatusCodes.Status409Conflict, "Conflict", e.Message),
            BusinessRuleException { Errors: not null } e =>
                ProblemDetailsFactory.ValidationErrors(e.Errors, e.Message),
            BusinessRuleException e =>
                ProblemDetailsFactory.BusinessRule(e.Message),
            ValidationException e =>
                ProblemDetailsFactory.ValidationErrors(e.Errors
                    .GroupBy(f => f.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray())),
            IdentityValidationException e =>
                ProblemDetailsFactory.ValidationErrors(e.Errors),
            _ => null
        };

        if (problemDetails is null)
            return false;

        httpContext.Response.StatusCode = problemDetails.Status!.Value;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}

public static class ProblemDetailsFactory
{
    private static readonly Dictionary<int, string> TypeUris = new()
    {
        [StatusCodes.Status400BadRequest] = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        [StatusCodes.Status401Unauthorized] = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        [StatusCodes.Status403Forbidden] = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        [StatusCodes.Status404NotFound] = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        [StatusCodes.Status409Conflict] = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
    };

    public static ProblemDetails Create(int statusCode, string title, string? detail = null)
        => new()
        {
            Type = TypeUris.GetValueOrDefault(statusCode),
            Title = title,
            Status = statusCode,
            Detail = detail
        };

    public static ProblemDetails BusinessRule(string detail)
        => Create(StatusCodes.Status400BadRequest, "Business rule violation", detail);

    public static IResult BusinessRuleResult(string detail)
        => Results.Json(BusinessRule(detail),
            statusCode: StatusCodes.Status400BadRequest,
            contentType: "application/problem+json");

    public static ProblemDetails Forbidden(string detail, string? errorCode = null)
    {
        var pd = Create(StatusCodes.Status403Forbidden, "Forbidden", detail);
        if (errorCode is not null)
            pd.Extensions["errorCode"] = errorCode;
        return pd;
    }

    public static ProblemDetails Unauthorized(string detail, string errorCode)
    {
        var pd = Create(StatusCodes.Status401Unauthorized, "Unauthorized", detail);
        pd.Extensions["errorCode"] = errorCode;
        return pd;
    }

    public static ProblemDetails ValidationErrors(object errors, string? detail = null)
    {
        var pd = Create(StatusCodes.Status400BadRequest, "One or more validation errors occurred.", detail);
        pd.Extensions["errors"] = errors;
        return pd;
    }
}
