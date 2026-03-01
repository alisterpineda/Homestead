using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Documents.v1.GetActivity;

public record GetActivityRequest : PaginatedRequest
{
    public Guid DocumentId { get; init; }
}
