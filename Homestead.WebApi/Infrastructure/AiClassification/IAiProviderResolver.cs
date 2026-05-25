using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Infrastructure.AiClassification;

public interface IAiProviderResolver
{
    Task<AiProvider?> ResolveAsync(Guid? providerId, CancellationToken ct);
}
