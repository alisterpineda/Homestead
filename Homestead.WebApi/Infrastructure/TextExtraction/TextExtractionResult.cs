namespace Homestead.WebApi.Infrastructure.TextExtraction;

public sealed class TextExtractionResult
{
    public string? Text { get; }
    public bool Succeeded => Error is null;
    public Exception? Error { get; }

    private TextExtractionResult(string? text, Exception? error)
    {
        Text = text;
        Error = error;
    }

    public static TextExtractionResult Success(string? text) => new(text, error: null);

    public static TextExtractionResult Failure(Exception error) => new(null, error);
}
