using System.Text.Json;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Activity.GetActivity;

public class GetActivityHandler(AppDbContext db)
    : IRequestHandler<GetActivityQuery, PaginatedResponse<GetActivityResponse>>
{
    private static readonly Dictionary<string, string> FieldDisplayNames = new()
    {
        ["Name"] = "name",
        ["DocumentDate"] = "document date",
        ["ExpiryDate"] = "expiry date",
        ["Notes"] = "notes",
        ["Summary"] = "summary",
        ["TagPaths"] = "tags",
        ["PartyIds"] = "parties",
        ["Label"] = "label",
        ["ExtractedText"] = "extracted text"
    };

    public async Task<PaginatedResponse<GetActivityResponse>> Handle(
        GetActivityQuery request, CancellationToken ct)
    {
        // LEFT join Users (GroupJoin + SelectMany + DefaultIfEmpty), NOT an inner Join: an audit row
        // whose actor is null (a system/background action) or a since-deleted user must still surface.
        // An inner join silently drops those rows — the bug called out in the Documents audit doc §3.1.
        var auditRows = await db.AuditLogs
            .AsNoTracking()
            .Where(a =>
                (a.EntityType == "Document" && a.EntityId == request.DocumentId) ||
                (a.EntityType == "DocumentVersion" && a.ParentEntityId == request.DocumentId))
            .GroupJoin(
                db.Users.AsNoTracking(),
                a => a.UserId,
                u => u.Id,
                (a, users) => new { Audit = a, Users = users })
            .SelectMany(
                x => x.Users.DefaultIfEmpty(),
                (x, u) => new
                {
                    x.Audit.Id,
                    x.Audit.EntityType,
                    x.Audit.Action,
                    x.Audit.Changes,
                    x.Audit.Timestamp,
                    DisplayName = u != null ? u.DisplayName : null,
                    Email = u != null ? u.Email : null,
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

        // Two distinct ID spaces (audit + comments) merge via Concat and paginate in memory, so Id is the
        // tiebreaker: equal-timestamp rows — likely on coarse-resolution clocks — get a stable order and
        // can't drift across page boundaries.
        var allItems = auditItems
            .Concat(commentItems)
            .OrderByDescending(x => x.Timestamp)
            .ThenByDescending(x => x.Id)
            .ToList();

        var totalCount = allItems.Count;
        var paged = allItems
            .Skip((request.EffectivePage - 1) * request.EffectivePageSize)
            .Take(request.EffectivePageSize)
            .ToList();

        return new PaginatedResponse<GetActivityResponse>
        {
            Items = paged,
            Page = request.EffectivePage,
            PageSize = request.EffectivePageSize,
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
