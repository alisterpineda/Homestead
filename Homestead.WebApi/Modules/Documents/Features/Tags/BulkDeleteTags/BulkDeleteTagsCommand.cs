using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.BulkDeleteTags;

public record BulkDeleteTagsCommand(List<Guid> Ids) : IRequest<BulkDeleteTagsResponse>;
