using Homestead.WebApi.Authorization;
using MediatR;
using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.DeleteDocumentVersion;

public record DeleteDocumentVersionCommand(Guid DocumentId, Guid Id) : ICommand<bool>, IRequireDocumentAuthorization
{
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
