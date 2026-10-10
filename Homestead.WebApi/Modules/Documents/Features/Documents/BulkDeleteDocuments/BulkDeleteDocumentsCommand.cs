namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkDeleteDocuments;

public record BulkDeleteDocumentsCommand(List<Guid> Ids);
