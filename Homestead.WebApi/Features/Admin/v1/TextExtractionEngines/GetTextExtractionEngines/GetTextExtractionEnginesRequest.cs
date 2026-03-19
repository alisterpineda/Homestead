using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.GetTextExtractionEngines;

public record GetTextExtractionEnginesRequest : PaginatedRequest
{
    public string? Search { get; init; }
}
