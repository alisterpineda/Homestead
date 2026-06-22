using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Modules.Documents.Processing;

public record BulkClassifyPayload(
    List<Guid> DocumentIds,
    ClassificationScope Scope = ClassificationScope.All);
