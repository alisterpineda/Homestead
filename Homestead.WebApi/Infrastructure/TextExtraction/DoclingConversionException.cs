namespace Homestead.WebApi.Infrastructure.TextExtraction;

// docling-serve answered, and the answer was "this document did not convert" (failure, skipped, or no
// Markdown at all). A plain Exception on purpose — not an OperationCanceledException, so
// TextExtractionHelper records it as an extraction failure, and not an HTTP shape, so JobFailureClassifier
// treats it as final: the same file converts the same way next time.
public class DoclingConversionException(string status, IReadOnlyList<string> errors)
    : Exception(BuildMessage(status, errors))
{
    public string Status { get; } = status;

    public IReadOnlyList<string> Errors { get; } = errors;

    private static string BuildMessage(string status, IReadOnlyList<string> errors) =>
        errors.Count == 0
            ? $"Docling conversion ended with status '{status}'."
            : $"Docling conversion ended with status '{status}': {string.Join("; ", errors)}";
}
