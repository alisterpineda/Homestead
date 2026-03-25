using MediatR;

namespace Homestead.WebApi.Features.Documents.Tags.BulkDeleteTags;

public record BulkDeleteTagsCommand(List<Guid> Ids) : IRequest<BulkDeleteTagsResponse>;
