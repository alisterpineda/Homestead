namespace Homestead.WebApi.Modules.Documents.Services;

public interface IAiClassificationService
{
    Task<ClassificationResult?> ClassifyAsync(
        ClassificationInput input,
        List<TagInfo> availableTags,
        List<PartyInfo> availableParties,
        ClassificationScope scope,
        CancellationToken ct);
}

// The document as the model sees it — sent as given. Production's PDF mode sets Pdf and leaves ExtractedText null
// (the PDF replaces the OCR text: Claude reads the text itself, and sending both would double the tokens) and
// PageImage null; otherwise the extracted text, plus a page-1 render when PageImage is set.
public sealed record ClassificationInput(string DocumentName, string? ExtractedText, byte[]? PageImage, byte[]? Pdf)
{
    public override string ToString() =>
        $"ClassificationInput {{ DocumentName = {DocumentName}, ExtractedText = {ExtractedText?.Length ?? 0} chars, " +
        $"PageImage = {PageImage?.Length ?? 0} bytes, Pdf = {Pdf?.Length ?? 0} bytes }}";
}
