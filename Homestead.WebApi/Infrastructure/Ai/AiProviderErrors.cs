using System.ClientModel;
using Anthropic.Exceptions;

namespace Homestead.WebApi.Infrastructure.Ai;

// The one place that knows each provider SDK's exception types. Microsoft.Extensions.AI has no common
// exception, so every SDK throws its own; callers (the structured-output fallback ladder, the job retry
// classifier) ask here instead of naming SDK types. A new provider type adds its SDK here and nowhere else.
public static class AiProviderErrors
{
    // The HTTP status (0 = the request got no answer at all) and message of an exception thrown by a
    // provider SDK. False when the exception did not come from one.
    public static bool TryGetStatus(Exception exception, out int status, out string message)
    {
        switch (exception)
        {
            // The OpenAI / Azure OpenAI SDKs (System.ClientModel). Status 0 is "no response".
            case ClientResultException ex:
                status = ex.Status;
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
