using Homestead.WebApi.Authorization;
using MediatR;
using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineAllSuggestions;

public record DeclineAllSuggestionsCommand : IRequest, ICommand, IRequireDocumentAuthorization
{
    public Guid DocumentId { get; init; }
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
