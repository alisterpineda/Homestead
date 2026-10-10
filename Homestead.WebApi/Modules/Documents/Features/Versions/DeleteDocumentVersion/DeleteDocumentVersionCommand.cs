namespace Homestead.WebApi.Modules.Documents.Features.Versions.DeleteDocumentVersion;

public record DeleteDocumentVersionCommand(Guid DocumentId, Guid Id);
