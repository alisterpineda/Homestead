using Microsoft.AspNetCore.Mvc;

namespace HomeSuiteHome.WebApi.Features.Tags.v1.GetTags;

public record GetTagsRequest
{
    [FromQuery]
    public string? ParentPath { get; init; }

    [FromQuery]
    public bool IncludeChildren { get; init; }

    [FromQuery]
    public string? Search { get; init; }
}
