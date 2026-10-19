using Anthropic.Exceptions;
using Anthropic.Models;

namespace Homestead.WebApi.Infrastructure.Ai;

// The one place that knows the AI SDK's exception types; callers (the job retry classifier, which reads the
// status, and the connection probe, which shows the message) ask here instead of naming SDK types. Another
// backend adds its SDK's exceptions here and nowhere else.
public static class AiProviderErrors
{
    // The HTTP status (0 = the request got no answer at all) and message of an exception thrown by the AI SDK.
    // False when the exception did not come from it.
    public static bool TryGetStatus(Exception exception, out int status, out string message)
    {
        switch (exception)
        {
            // An error event after the HTTP 200 of a streamed reply (e.g. the model overloaded mid-stream). Not an
            // AnthropicApiException (it derives from AnthropicServiceException), so it has no status code of its
            // own: the event's error type stands in for one, so an overload still reads as transient.
            case AnthropicSseException ex:
                status = ex.ErrorType switch
                {
                    ErrorType.OverloadedError => 529,
                    ErrorType.ApiError => 500,
                    ErrorType.RateLimitError => 429,
                    _ => 0,
                };
                message = ex.Message;
                return true;

            // A stream cut off part-way (or a body the SDK could not read): the request got no usable answer.
            case AnthropicInvalidDataException ex:
                status = 0;
                message = ex.Message;
                return true;

            // The Anthropic SDK. Message carries the response body, which is what names a rejected option.
            case AnthropicApiException ex:
                status = (int)ex.StatusCode;
                message = ex.Message;
                return true;

            // The Anthropic SDK's network failure: the request never got an answer.
            case AnthropicIOException ex:
                status = 0;
                message = ex.Message;
                return true;

            default:
                status = 0;
                message = string.Empty;
                return false;
        }
    }
}
