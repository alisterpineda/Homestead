using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Infrastructure.AiClassification;

public interface IAiClassificationService
{
    Task<ClassificationResult?> ClassifyAsync(
        AiProvider provider,
        string documentName,
        string? extractedText,
        List<TagInfo> availableTags,
        CancellationToken ct);
}
