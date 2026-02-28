namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteTags;

public record BulkDeleteTagsRequest(List<Guid> Ids);
