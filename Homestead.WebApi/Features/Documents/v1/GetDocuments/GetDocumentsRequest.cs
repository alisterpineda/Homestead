using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Features.Documents.v1.GetDocuments;

public record GetDocumentsRequest : PaginatedRequest
{
    [FromQuery(Name = "PartyIds")]
    public Guid[]? PartyIds { get; init; }
}
