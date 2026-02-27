using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Features.Documents.v1.GetTags;

public record GetTagsRequest
{
    [FromQuery]
    public string? ParentPath { get; init; }

    [FromQuery]
    public bool IncludeChildren { get; init; }

    [FromQuery]
    public string? Search { get; init; }
}
