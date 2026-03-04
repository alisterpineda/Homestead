namespace Homestead.WebApi.Features.Documents.v1.GetDocumentThumbnail;

public record GetDocumentThumbnailRequest(Guid Id, Guid? VersionId);
