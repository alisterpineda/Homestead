using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Activity.GetActivity;

public record GetActivityQuery : PaginatedRequest
{
    public Guid DocumentId { get; init; }
}
