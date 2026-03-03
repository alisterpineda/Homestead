namespace Homestead.WebApi.Features.Documents.v1.GetDocumentFile;

public record GetDocumentFileRequest(Guid Id, Guid? VersionId, bool? Download = null);
