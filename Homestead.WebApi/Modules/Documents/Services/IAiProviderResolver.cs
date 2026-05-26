using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Modules.Documents.Services;

public interface IAiProviderResolver
{
    Task<AiProvider?> ResolveAsync(Guid? providerId, CancellationToken ct);
}
