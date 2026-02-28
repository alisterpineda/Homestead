namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteParties;

public record BulkDeletePartiesRequest(List<Guid> Ids);
