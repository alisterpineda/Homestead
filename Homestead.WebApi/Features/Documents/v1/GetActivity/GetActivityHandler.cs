using System.Text.Json;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetActivity;

public class GetActivityHandler(AppDbContext db, IAuthorizationService authService, IHttpContextAccessor httpContext)
    : IRequestHandler<GetActivityRequest, PaginatedResponse<GetActivityResponse>>
{
    private static readonly Dictionary<string, string> FieldDisplayNames = new()
    {
        ["Name"] = "name",
        ["DocumentDate"] = "document date",
        ["ValidFrom"] = "valid from",
        ["ValidUntil"] = "valid until",
        ["ExpiryDate"] = "expiry date",
        ["TagPaths"] = "tags",
        ["PartyIds"] = "parties",
        ["Label"] = "label",
        ["ExtractedText"] = "extracted text"
    };

    public async Task<PaginatedResponse<GetActivityResponse>> Handle(
        GetActivityRequest request, CancellationToken ct)
    {
        await authService.AuthorizeDocumentAsync(httpContext.HttpContext!.User, request.DocumentId, DocumentOperations.Read);

        var auditRows = await db.AuditLogs
            .AsNoTracking()
            .Where(a =>
                (a.EntityType == "Document" && a.EntityId == request.DocumentId) ||
                (a.EntityType == "DocumentVersion" && a.ParentEntityId == request.DocumentId))
            .Join(
                db.Users.AsNoTracking(),
                a => a.UserId,
                u => u.Id,
                (a, u) => new
                {
                    a.Id,
                    a.EntityType,
                    a.Action,
                    a.Changes,
                    a.Timestamp,
                    u.DisplayName,
                    u.Email
                })
            .ToListAsync(ct);

        var auditItems = auditRows.Select(x =>
        {
            var userName = x.DisplayName ?? x.Email ?? "Someone";
            return new GetActivityResponse
            {
                Type = "audit_log",
                Id = x.Id,
                Timestamp = x.Timestamp,
                UserDisplayName = x.DisplayName,
                UserEmail = x.Email,
                Summary = GenerateSummary(x.EntityType, x.Action, x.Changes, userName)
            };
        });

        var commentItems = await db.DocumentComments
            .AsNoTracking()
            .Where(c => c.DocumentId == request.DocumentId)
            .GroupJoin(
                db.Users.AsNoTracking(),
                c => c.CreatedByUserId,
                u => u.Id,
                (c, users) => new { Comment = c, Users = users })
            .SelectMany(
                x => x.Users.DefaultIfEmpty(),
                (x, u) => new GetActivityResponse
                {
                    Type = "comment",
                    Id = x.Comment.Id,
                    Timestamp = x.Comment.CreatedAt,
                    UserDisplayName = u != null ? u.DisplayName : null,
                    UserEmail = u != null ? u.Email : null,
                    Body = x.Comment.Body
                })
            .ToListAsync(ct);

        var allItems = auditItems
            .Concat(commentItems)
            .OrderByDescending(x => x.Timestamp)
            .ToList();

        var totalCount = allItems.Count;
        var paged = allItems
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new PaginatedResponse<GetActivityResponse>
        {
            Items = paged,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }

    private static string GenerateSummary(string entityType, AuditAction action, string? changes, string userName)
    {
        return action switch
        {
            AuditAction.Created => entityType switch
            {
                "Document" => $"{userName} created this document",
                "DocumentVersion" => $"{userName} added a new version",
                _ => $"{userName} created a {entityType.ToLowerInvariant()}"
            },
            AuditAction.Deleted => entityType switch
            {
                "Document" => $"{userName} deleted this document",
                "DocumentVersion" => $"{userName} deleted a version",
                _ => $"{userName} deleted a {entityType.ToLowerInvariant()}"
            },
            AuditAction.Updated => GenerateUpdatedSummary(entityType, changes, userName),
            AuditAction.SoftDeleted => entityType switch
            {
                "Document" => $"{userName} moved this document to trash",
                _ => $"{userName} deleted a {entityType.ToLowerInvariant()}"
            },
            AuditAction.Restored => entityType switch
            {
                "Document" => $"{userName} restored this document from trash",
                _ => $"{userName} restored a {entityType.ToLowerInvariant()}"
            },
            _ => $"{userName} modified this {entityType.ToLowerInvariant()}"
        };
    }

    private static string GenerateUpdatedSummary(string entityType, string? changesJson, string userName)
    {
        var fieldNames = ParseChangedFieldNames(changesJson);

        if (fieldNames.Count == 0)
        {
            return entityType switch
            {
                "Document" => $"{userName} updated this document",
                "DocumentVersion" => $"{userName} updated a version",
                _ => $"{userName} updated a {entityType.ToLowerInvariant()}"
            };
        }

        var displayNames = fieldNames
            .Select(f => FieldDisplayNames.TryGetValue(f, out var display) ? display : f.ToLowerInvariant())
            .ToList();

        var fieldList = string.Join(", ", displayNames);

        return entityType switch
        {
            "Document" => $"{userName} updated {fieldList}",
            "DocumentVersion" => $"{userName} updated version {fieldList}",
            _ => $"{userName} updated {fieldList}"
        };
    }

    private static List<string> ParseChangedFieldNames(string? changesJson)
    {
        if (string.IsNullOrEmpty(changesJson))
            return [];

        try
        {
            var changes = JsonSerializer.Deserialize<List<FieldChange>>(changesJson, FieldChange.JsonOptions);
            return changes?.Select(c => c.Field).Where(f => !string.IsNullOrEmpty(f)).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
