using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public sealed class TextExtractionResult
{
    public string? Text { get; }

    // Provenance for the extraction that produced Text. Null on failure — there is no meaningful
    // "how this text was produced" when no text was produced.
    public TextExtractionMetadata? Metadata { get; }

    public bool Succeeded => Error is null;
    public Exception? Error { get; }

    private TextExtractionResult(string? text, TextExtractionMetadata? metadata, Exception? error)
    {
        Text = text;
        Metadata = metadata;
        Error = error;
    }

    public static TextExtractionResult Success(string? text, TextExtractionMetadata metadata) =>
        new(text, metadata, error: null);

    public static TextExtractionResult Failure(Exception error) => new(null, null, error);
}
