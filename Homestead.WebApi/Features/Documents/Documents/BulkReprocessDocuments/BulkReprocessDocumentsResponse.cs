namespace Homestead.WebApi.Features.Documents.Documents.BulkReprocessDocuments;

public record BulkReprocessDocumentsResponse(List<Guid> JobIds);
