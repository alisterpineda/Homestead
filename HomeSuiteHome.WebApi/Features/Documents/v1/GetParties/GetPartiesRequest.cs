using HomeSuiteHome.WebApi.Common;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetParties;

public record GetPartiesRequest : PaginatedRequest
{
    public string? Search { get; init; }
}
