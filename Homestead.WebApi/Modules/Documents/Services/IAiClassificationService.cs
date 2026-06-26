namespace Homestead.WebApi.Modules.Documents.Services;

public interface IAiClassificationService
{
    Task<ClassificationResult?> ClassifyAsync(
        string documentName,
        string? extractedText,
        byte[]? pageImage,
        List<TagInfo> availableTags,
        List<PartyInfo> availableParties,
        ClassificationScope scope,
        CancellationToken ct);
}
