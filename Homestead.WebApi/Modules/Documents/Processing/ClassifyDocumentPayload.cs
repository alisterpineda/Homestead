namespace Homestead.WebApi.Modules.Documents.Processing;

public record ClassifyDocumentPayload(Guid DocumentId, Guid? AiProviderId = null);
