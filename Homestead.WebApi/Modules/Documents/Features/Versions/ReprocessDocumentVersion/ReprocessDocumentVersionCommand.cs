using Homestead.WebApi.Authorization;
using MediatR;
using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.ReprocessDocumentVersion;

public class ReprocessDocumentVersionCommand : ICommand<ReprocessDocumentVersionResponse>, IRequireDocumentAuthorization
{
    public Guid DocumentId { get; set; }
    public Guid VersionId { get; set; }
    public Guid? TextExtractionEngineId { get; set; }

    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
