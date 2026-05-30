using Homestead.WebApi.Authorization;
using MediatR;
using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.CreateDocumentVersion;

public class CreateDocumentVersionCommand : ICommand<CreateDocumentVersionResponse>, IRequireDocumentAuthorization
{
    public Guid DocumentId { get; set; }
    public string? Label { get; set; }
    public required IFormFile File { get; set; }

    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
