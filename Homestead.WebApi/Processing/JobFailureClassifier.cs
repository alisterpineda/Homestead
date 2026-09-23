using System.ClientModel;
using System.Net;
using Azure;
using Npgsql;
using Polly;

namespace Homestead.WebApi.Processing;

/// <summary>
/// The default answer to "is this failure worth another attempt?" — <c>IJobHandler.ShouldRetry</c>
/// unless a handler overrides it.
/// </summary>
/// <remarks>
/// An <b>allow-list</b> of known-transient shapes, not "anything that isn't a structured error". Most of
/// this codebase's deterministic failures are plain <see cref="InvalidOperationException"/> (a missing
/// payload, a document that isn't there) or an HTTP 4xx surfaced by <c>EnsureSuccessStatusCode</c>;
/// a deny-list would spend every attempt on them and turn an instant, actionable failure into a slow one.
/// </remarks>
public static class JobFailureClassifier
{
    public static bool IsTransient(Exception exception)
    {
        // A structured error is the handler's own, deliberate verdict on what went wrong — a duplicate,
        // a blocked source, a parse that found no text. It is final whatever it happens to wrap.
        if (exception is IStructuredJobError)
            return false;

        // The job's own budget ran out. Another attempt gets the same budget.
        if (exception is JobTimeoutException)
            return false;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (IsTransientShape(current))
                return true;
        }

        return false;
    }

    private static bool IsTransientShape(Exception exception) => exception switch
    {
        // No status code: the request never got an answer (DNS, refused connection, reset socket).
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } status } => IsTransientStatus((int)status),

        // The OpenAI / Azure OpenAI SDKs. Status 0 is the same "no answer at all" as above.
        ClientResultException { Status: var status } => status == 0 || IsTransientStatus(status),

        // Azure SDKs (Document Intelligence).
        RequestFailedException { Status: var status } => status == 0 || IsTransientStatus(status),

        // An HttpClient timeout. The processor's own timeout and shutdown are ruled out before a handler's
        // ShouldRetry is ever asked, so a cancellation that reaches here came from an outbound call.
        TaskCanceledException or TimeoutException => true,

        // The resilience pipeline's circuit breaker or rate limiter refused the call outright.
        ExecutionRejectedException => true,

        NpgsqlException { IsTransient: true } => true,

        _ => false,
    };

    private static bool IsTransientStatus(int status) =>
        status is (int)HttpStatusCode.RequestTimeout or (int)HttpStatusCode.TooManyRequests or >= 500;
}
