using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Modules.Documents.Processing;

public record ClassifyDocumentPayload(
    Guid DocumentId,
    ClassificationScope Scope = ClassificationScope.All);
