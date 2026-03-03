using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Authorization;

public static class DocumentOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Write = new() { Name = nameof(Write) };
}
