using System.ComponentModel;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Homestead.WebApi.Modules.Mcp;

// Instantiated per call by the SDK from the request's services — never register it in DI (ignored).
[McpServerToolType]
public sealed class WhoAmITools(
    ICurrentUserAccessor currentUser, AppDbContext db, McpToolInvoker invoker, GetDocumentsHandler getDocuments)
{
    [McpServerTool(Name = "whoami", Title = "Who am I", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Your Homestead identity and whether you may edit. Call once at the start of a session.")]
    public async Task<WhoAmIResult> WhoAmI(CancellationToken ct)
    {
        // The McpActiveUser policy guarantees a linked, active user; the row is what the principal was built from.
        var userId = currentUser.UserId ?? throw new McpException("No Homestead account is linked to this identity.");
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new McpException("No Homestead account is linked to this identity.");

        // Two counts through the same access-filtered handler search_documents uses (active documents only), so
        // they equal what search_documents(has_tags=false) / (pending_review=true) would report.
        var (untagged, pendingReview) = await invoker.ReadAsync(async c =>
        {
            var untaggedCount = await CountAsync(new GetDocumentsQuery { HasTags = false }, c);
            var pendingCount = await CountAsync(new GetDocumentsQuery { HasPendingSuggestions = true }, c);
            return (untaggedCount, pendingCount);
        }, ct);

        return new WhoAmIResult(user.Email, user.DisplayName, user.Role.ToString(),
            CanWrite: user.Role is AppRole.Admin or AppRole.Contributor,
            UntaggedCount: untagged, PendingReviewCount: pendingReview);
    }

    private async Task<int> CountAsync(GetDocumentsQuery query, CancellationToken ct) =>
        (await getDocuments.HandleAsync(query with { PageSize = 1 }, ct)).TotalCount;
}

public sealed record WhoAmIResult(
    string Email, string DisplayName, string Role, bool CanWrite, int UntaggedCount, int PendingReviewCount);
