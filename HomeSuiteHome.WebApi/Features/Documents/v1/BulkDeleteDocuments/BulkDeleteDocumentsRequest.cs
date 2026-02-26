namespace HomeSuiteHome.WebApi.Features.Documents.v1.BulkDeleteDocuments;

public record BulkDeleteDocumentsRequest(List<Guid> Ids);
