using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItemActivity;

public class GetItemActivityHandler(AppDbContext db)
    : IRequestHandler<GetItemActivityQuery, PaginatedResponse<GetItemActivityResponse>?>
{
    // The one item audit field → human label map (the durable-only projection it superseded is gone).
    // AreaId/ParentItemId both read as "location" (a move emits one or both) and Value/ValueCurrencyId
    // both as "value" (they travel as a both-or-neither pair); the summary de-dupes so a paired change
    // reads once, not "location, location" / "value, value".
    private static readonly Dictionary<string, string> FieldDisplayNames = new()
    {
        ["Name"] = "name",
        ["Status"] = "status",
        ["Value"] = "value",
        ["ValueCurrencyId"] = "value",
        ["AreaId"] = "location",
        ["ParentItemId"] = "location",
        ["Compartment"] = "compartment",
        ["Serial"] = "serial",
        ["AcquiredOn"] = "acquired date",
        ["LentTo"] = "lent to",
        // Current rows record the container flag under "IsContainer"; historical rows recorded the
        // interim "DurableKind" trichotomy (and, before that, an older "IsContainer" boolean). All map
        // to "type" so activity reads consistently across the schema's history.
        ["IsContainer"] = "type",
        ["DurableKind"] = "type",
        // The column and audit field stay "Notes"; the UI calls the field Description, so historical
        // and future rows both read "updated description".
        ["Notes"] = "description",
        ["Unit"] = "unit",
        ["RestockThreshold"] = "restock threshold",
    };

    /// <summary>
    /// The ItemWarranty field → label map, kept separate from the item's because the two disagree on
    /// "Notes": on the item that column is the Description field, on a warranty it really is a note.
    /// </summary>
    private static readonly Dictionary<string, string> WarrantyFieldDisplayNames = new()
    {
        ["Provider"] = "provider",
        ["ExpiresOn"] = "expiry",
        ["Notes"] = "notes",
    };

    public async Task<PaginatedResponse<GetItemActivityResponse>?> Handle(
        GetItemActivityQuery request, CancellationToken ct)
    {
        // Both kinds: a consumable's feed is its notes plus its metadata audit rows (its movement
        // ledger lives on its own tab). Null only when the item is missing.
        var exists = await db.Items.AsNoTracking().AnyAsync(i => i.Id == request.Id, ct);
        if (!exists)
            return null;

        var auditRows = await LoadAuditRowsAsync(request.Id, ct);
        var noteRows = await LoadNoteRowsAsync(request.Id, ct);

        // Merge + order + paginate in memory (the audit summaries parse Changes JSON, so the audit half
        // can't be sorted in SQL anyway). The sort key is explicit: effective date, then the recorded
        // time (a note's CreatedAt / an audit row's Timestamp), then Id — all descending — so
        // equal-date rows get a stable order that can't drift across page boundaries.
        var all = auditRows
            .Concat(noteRows)
            .OrderByDescending(x => x.Key.Effective)
            .ThenByDescending(x => x.Key.Recorded)
            .ThenByDescending(x => x.Key.Id)
            .Select(x => x.Row)
            .ToList();

        var paged = all
            .Skip((request.EffectivePage - 1) * request.EffectivePageSize)
            .Take(request.EffectivePageSize)
            .ToList();

        return new PaginatedResponse<GetItemActivityResponse>
        {
            Items = paged,
            Page = request.EffectivePage,
            PageSize = request.EffectivePageSize,
            TotalCount = all.Count,
        };
    }

    private record SortKey(DateTimeOffset Effective, DateTimeOffset Recorded, Guid Id);

    private async Task<List<(SortKey Key, GetItemActivityResponse Row)>> LoadAuditRowsAsync(Guid itemId, CancellationToken ct)
    {
        // The item's own rows plus exactly one ParentEntityId branch: ItemWarranty (the DocumentVersion
        // shape). A warranty has no feed row of its own, so its lifecycle is the only trace of it —
        // unlike InventoryNote rows (created/updated/deleted notes, also parented to this item), which
        // stay deliberately unprojected because the notes themselves are the feed rows and a deleted
        // note simply disappears (activity.md §Auditing notes, warranties.md §Audit).
        //
        // LEFT join Users (GroupJoin + SelectMany + DefaultIfEmpty), NOT an inner Join: an audit row
        // whose actor is null (a system action) or a since-deleted user must still surface. An inner
        // join silently drops those rows — the bug called out in the Documents audit doc §3.1.
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a =>
                (a.EntityType == "Item" && a.EntityId == itemId) ||
                (a.EntityType == "ItemWarranty" && a.ParentEntityId == itemId))
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

        return rows
            .Select(x =>
            {
                var userName = x.DisplayName ?? x.Email ?? "Someone";
                var row = new GetItemActivityResponse
                {
                    Type = "audit_log",
                    Id = x.Id,
                    Timestamp = x.Timestamp,
                    UserDisplayName = x.DisplayName,
                    UserEmail = x.Email,
                    Summary = GenerateSummary(x.EntityType, x.Action, x.Changes, userName),
                };
                return (new SortKey(x.Timestamp, x.Timestamp, x.Id), row);
            })
            .ToList();
    }

    private async Task<List<(SortKey Key, GetItemActivityResponse Row)>> LoadNoteRowsAsync(
        Guid itemId, CancellationToken ct)
    {
        // Optional navs → LEFT JOINs; a note whose author was deleted (SetNull FK) still renders.
        var rows = await db.ItemNotes.AsNoTracking()
            .Where(n => n.ItemId == itemId)
            .Select(n => new GetItemActivityResponse
            {
                Type = "note",
                Id = n.Id,
                Timestamp = n.OccurredAt,
                UserDisplayName = n.CreatedByUser != null ? n.CreatedByUser.DisplayName : null,
                UserEmail = n.CreatedByUser != null ? n.CreatedByUser.Email : null,
                Body = n.Body,
                OccurredAt = n.OccurredAt,
                CreatedAt = n.CreatedAt,
                UpdatedAt = n.UpdatedAt,
                IsEdited = n.UpdatedAt != n.CreatedAt,
                UpdatedByDisplayName = n.UpdatedByUser != null ? n.UpdatedByUser.DisplayName : null,
                UpdatedByEmail = n.UpdatedByUser != null ? n.UpdatedByUser.Email : null,
            })
            .ToListAsync(ct);

        return rows
            .Select(r => (new SortKey(r.OccurredAt!.Value, r.CreatedAt!.Value, r.Id), r))
            .ToList();
    }

    private static string GenerateSummary(string entityType, AuditAction action, string? changes, string userName)
    {
        if (entityType == "ItemWarranty")
            return GenerateWarrantySummary(action, changes, userName);

        return action switch
        {
            AuditAction.Created => $"{userName} added this item",
            AuditAction.Deleted => $"{userName} deleted this item",
            AuditAction.Updated => GenerateUpdatedSummary(changes, userName),
            AuditAction.SoftDeleted => $"{userName} moved this item to trash",
            AuditAction.Restored => $"{userName} restored this item",
            _ => $"{userName} modified this item",
        };
    }

    private static string GenerateUpdatedSummary(string? changesJson, string userName)
    {
        var fieldNames = ParseChangedFieldNames(changesJson);
        if (fieldNames.Count == 0)
            return $"{userName} updated this item";

        var displayNames = fieldNames
            .Select(f => FieldDisplayNames.TryGetValue(f, out var display) ? display : f.ToLowerInvariant())
            .Distinct()
            .ToList();

        return $"{userName} updated {string.Join(", ", displayNames)}";
    }

    /// <summary>
    /// A warranty has no feed row of its own, so these three summaries are the whole trace of it. The
    /// delete carries the one-line description the delete handler recorded ("Bosch · until 2028-03-12"),
    /// which is the only place a removed warranty's details survive.
    /// </summary>
    private static string GenerateWarrantySummary(AuditAction action, string? changesJson, string userName) =>
        action switch
        {
            AuditAction.Created => $"{userName} added a warranty",
            AuditAction.Updated => GenerateWarrantyUpdatedSummary(changesJson, userName),
            AuditAction.Deleted => $"{userName} removed a warranty{DescriptionSuffix(changesJson)}",
            _ => $"{userName} modified a warranty",
        };

    private static string GenerateWarrantyUpdatedSummary(string? changesJson, string userName)
    {
        var fieldNames = ParseChangedFieldNames(changesJson);
        if (fieldNames.Count == 0)
            return $"{userName} updated a warranty";

        var displayNames = fieldNames
            .Select(f => WarrantyFieldDisplayNames.TryGetValue(f, out var display) ? display : f.ToLowerInvariant())
            .Distinct()
            .ToList();

        return $"{userName} updated a warranty ({string.Join(", ", displayNames)})";
    }

    /// <summary>
    /// The audit logger records a deletion's description as a "Description" field change; surface it in
    /// parentheses when present, and say nothing when it isn't.
    /// </summary>
    private static string? DescriptionSuffix(string? changesJson)
    {
        var description = ParseChanges(changesJson)
            .OfType<ScalarFieldChange>()
            .FirstOrDefault(c => c.Field == "Description")
            ?.OldValue;

        return string.IsNullOrWhiteSpace(description) ? null : $" ({description})";
    }

    private static List<string> ParseChangedFieldNames(string? changesJson) =>
        ParseChanges(changesJson).Select(c => c.Field).Where(f => !string.IsNullOrEmpty(f)).ToList();

    private static List<FieldChange> ParseChanges(string? changesJson)
    {
        if (string.IsNullOrEmpty(changesJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<FieldChange>>(changesJson, FieldChange.JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
