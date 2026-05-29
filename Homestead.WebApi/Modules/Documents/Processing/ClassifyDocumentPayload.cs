using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Modules.Documents.Processing;

public record ClassifyDocumentPayload(
    Guid DocumentId,
    Guid? AiProviderId = null,
    ClassificationScope Scope = ClassificationScope.All);
