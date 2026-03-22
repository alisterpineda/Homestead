using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetParties;

public record GetPartiesRequest : PaginatedRequest, IRequest<PaginatedResponse<GetPartiesResponse>>
{
    public string? Search { get; init; }
    public PartySortBy? SortBy { get; init; }
    public SortDirection? SortDir { get; init; }
}
