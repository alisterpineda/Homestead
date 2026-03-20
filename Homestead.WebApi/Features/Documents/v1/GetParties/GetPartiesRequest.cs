using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Documents.v1.GetParties;

public record GetPartiesRequest : PaginatedRequest
{
    public string? Search { get; init; }
    public PartySortBy? SortBy { get; init; }
    public SortDirection? SortDir { get; init; }
}
