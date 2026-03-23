using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteTags;

public record BulkDeleteTagsCommand(List<Guid> Ids) : IRequest<BulkDeleteTagsResponse>;
