namespace Homestead.WebApi.Features.Documents.v1.BulkUpdateDocuments;

public record BulkUpdateDocumentsRequest(List<Guid> Ids, List<string>? TagPaths, List<Guid>? PartyIds);
