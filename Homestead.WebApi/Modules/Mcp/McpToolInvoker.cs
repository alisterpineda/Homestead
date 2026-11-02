using System.Text.Json;
using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using ModelContextProtocol;

namespace Homestead.WebApi.Modules.Mcp;

// The one frame every MCP tool runs its work in — what DomainExceptionHandler, the endpoint filters and
// CommandTransactionFilter are for /api, since none of them runs on /mcp:
// - Authorization by document id is part of the signature (ReadDocumentAsync / WriteDocumentAsync), so a tool cannot
//   forget it. Non-admins get AccessDeniedException for nonexistent ids too, hence "Not found or no access".
// - Writes are role-gated, then run in one IUnitOfWork transaction (committed once, rolled back on any throw).
// - Domain exceptions become McpException, the only kind whose message reaches the client. Anything unmapped
//   (including OperationCanceledException and an McpException thrown by work) propagates untouched. No logging
//   here: the SDK already logs every McpException.
public sealed class McpToolInvoker(
    IAuthorizationService authz, IHttpContextAccessor http, ICurrentUserAccessor currentUser, IUnitOfWork unitOfWork)
{
    public const string ReadOnlyRoleMessage = "Access denied: your Homestead role is read-only.";

    public async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        try
        {
            return await work(ct);
        }
        catch (Exception e) when (Map(e) is { } mapped)
        {
            throw mapped;
        }
    }

    public Task<T> ReadDocumentAsync<T>(Guid documentId, Func<CancellationToken, Task<T>> work, CancellationToken ct) =>
        ReadAsync(async c =>
        {
            await AuthorizeDocumentAsync(documentId, DocumentOperations.Read);
            return await work(c);
        }, ct);

    // The role gate runs before the transaction frame. Only the two bulk handlers authorize documents themselves;
    // everything else taking a document id (incl. the suggestion handlers) goes through WriteDocumentAsync.
    public async Task<T> WriteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (currentUser.Role is not (AppRole.Admin or AppRole.Contributor))
            throw new McpException(ReadOnlyRoleMessage);

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(() => work(ct), ct);
        }
        catch (Exception e) when (Map(e) is { } mapped)
        {
            throw mapped;
        }
    }

    // Authorizes inside the frame but before any write, so a denial never opens the lazy transaction.
    public Task<T> WriteDocumentAsync<T>(Guid documentId, Func<CancellationToken, Task<T>> work, CancellationToken ct) =>
        WriteAsync(async c =>
        {
            await AuthorizeDocumentAsync(documentId, DocumentOperations.Write);
            return await work(c);
        }, ct);

    private Task AuthorizeDocumentAsync(Guid documentId, OperationAuthorizationRequirement operation) =>
        authz.AuthorizeDocumentAsync(
            http.HttpContext?.User ?? throw new AccessDeniedException(), documentId, operation);

    // DomainExceptionHandler's order. Null = not ours: the exception filter leaves it (and its stack) untouched.
    private static McpException? Map(Exception exception) => exception switch
    {
        AccessDeniedException e => new McpException("Not found or no access: " + e.Message, e),
        EntityNotFoundException e => new McpException("Not found: " + e.Message, e),
        DuplicateEntityException e => new McpException("Conflict: " + e.Message, e),
        EntityInUseException e => new McpException("Conflict: " + e.Message, e),
        BusinessRuleException { Errors: not null } e =>
            new McpException(e.Message + " " + JsonSerializer.Serialize(e.Errors), e),
        BusinessRuleException e => new McpException(e.Message, e),
        ValidationException e => new McpException(
            "Invalid input: " + string.Join("\n", e.Errors.Select(f =>
                string.IsNullOrEmpty(f.PropertyName) ? f.ErrorMessage : $"{f.PropertyName}: {f.ErrorMessage}")),
            e),
        // TagPathResolver's "Tag path cannot be empty." and the resolvers' own input checks.
        ArgumentException e => new McpException("Invalid input: " + e.Message, e),
        _ => null,
    };
}
