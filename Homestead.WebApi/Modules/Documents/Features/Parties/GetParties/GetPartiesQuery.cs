using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.GetParties;

public record GetPartiesQuery : PaginatedRequest
{
    public string? Search { get; init; }
    public PartySortBy? SortBy { get; init; }
    public SortDirection? SortDir { get; init; }
}
