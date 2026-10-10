namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocumentFile;

public record GetDocumentFileQuery(Guid Id, Guid? VersionId, bool? Download = null);
