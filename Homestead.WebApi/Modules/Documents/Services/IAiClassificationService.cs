using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Modules.Documents.Services;

public interface IAiClassificationService
{
    Task<ClassificationResult?> ClassifyAsync(
        AiProvider provider,
        string documentName,
        string? extractedText,
        List<TagInfo> availableTags,
        List<PartyInfo> availableParties,
        ClassificationScope scope,
        CancellationToken ct);
}
