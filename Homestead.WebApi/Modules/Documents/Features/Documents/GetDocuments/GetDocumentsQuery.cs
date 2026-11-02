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

    /// <summary>Inclusive lower bound on <c>DocumentDate</c>; documents without a date never match a bound.</summary>
    [FromQuery(Name = "DocumentDateFrom")]
    public DateOnly? DocumentDateFrom { get; init; }

    /// <summary>Inclusive upper bound on <c>DocumentDate</c>.</summary>
    [FromQuery(Name = "DocumentDateTo")]
    public DateOnly? DocumentDateTo { get; init; }

    /// <summary>Inclusive lower bound on <c>ExpiryDate</c>; documents without an expiry never match a bound.</summary>
    [FromQuery(Name = "ExpiryDateFrom")]
    public DateOnly? ExpiryDateFrom { get; init; }

    /// <summary>Inclusive upper bound on <c>ExpiryDate</c>.</summary>
    [FromQuery(Name = "ExpiryDateTo")]
    public DateOnly? ExpiryDateTo { get; init; }

    /// <summary><c>true</c> = at least one tag, <c>false</c> = untagged, null = no filter.</summary>
    [FromQuery(Name = "HasTags")]
    public bool? HasTags { get; init; }

    /// <summary><c>true</c> = at least one linked party, <c>false</c> = none, null = no filter.</summary>
    [FromQuery(Name = "HasParties")]
    public bool? HasParties { get; init; }

    /// <summary><c>true</c> = at least one pending suggestion, <c>false</c> = none pending, null = no filter.</summary>
    [FromQuery(Name = "HasPendingSuggestions")]
    public bool? HasPendingSuggestions { get; init; }
}
