using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Modules.Documents.Processing;

public record BulkClassifyPayload(
    List<Guid> DocumentIds,
    Guid? AiProviderId = null,
    ClassificationScope Scope = ClassificationScope.All);
