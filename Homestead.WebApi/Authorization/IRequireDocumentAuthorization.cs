using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Authorization;

public interface IRequireDocumentAuthorization
{
    Guid DocumentId { get; }
    OperationAuthorizationRequirement RequiredOperation { get; }
}
