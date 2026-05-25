using Homestead.WebApi.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineSuggestion;

public record DeclineSuggestionCommand : IRequest, IRequireDocumentAuthorization
{
    public Guid DocumentId { get; init; }
    public Guid SuggestionId { get; init; }
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
