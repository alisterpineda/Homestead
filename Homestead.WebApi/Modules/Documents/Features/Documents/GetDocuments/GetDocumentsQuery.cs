using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

public record GetDocumentsQuery : PaginatedRequest
{
    [FromQuery(Name = "PartyIds")]
    public Guid[]? PartyIds { get; init; }

    [FromQuery(Name = "TagIds")]
    public Guid[]? TagIds { get; init; }

    [FromQuery(Name = "IncludeChildTags")]
    public bool IncludeChildTags { get; init; }

    [FromQuery(Name = "Search")]
    public string? Search { get; init; }

    [FromQuery(Name = "SortBy")]
    public DocumentSortBy? SortBy { get; init; }

    [FromQuery(Name = "SortDir")]
    public SortDirection? SortDir { get; init; }

    [FromQuery(Name = "GroupId")]
    public Guid? GroupId { get; init; }

    [FromQuery(Name = "Deleted")]
    public bool? Deleted { get; init; }
}
