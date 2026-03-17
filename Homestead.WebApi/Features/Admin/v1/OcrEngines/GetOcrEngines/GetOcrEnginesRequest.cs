using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.GetOcrEngines;

public record GetOcrEnginesRequest : PaginatedRequest
{
    public string? Search { get; init; }
}
