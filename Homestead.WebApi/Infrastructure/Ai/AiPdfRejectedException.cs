namespace Homestead.WebApi.Infrastructure.Ai;

// The provider could not take a PDF at all — e.g. an Ai:Endpoint proxy with no file-upload route — as distinct from
// an error that would fail any request the same way (a bad key, model ID or endpoint). A caller that has another
// way to send the document (DocumentClassifier's text + render path) treats it as a PDF rejection and retries that
// way. Status is the HTTP status the provider got.
public class AiPdfRejectedException(string message, int status, Exception innerException)
    : Exception(message, innerException)
{
    public int Status { get; } = status;
}
