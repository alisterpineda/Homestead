using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Search;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItemActivity;

public class GetItemActivityHandler(AppDbContext db)
    : IRequestHandler<GetItemActivityQuery, PaginatedResponse<GetItemActivityResponse>?>
{
    // Duplicated verbatim from GetDurableActivityHandler for one commit: that handler (and its endpoint)
    // stays until Phase 2 swaps the durable detail page onto this feed and deletes it.
    //
    // Item audit field → human label. AreaId/ParentItemId both read as "location" (a move emits one
    // or both) and Value/ValueCurrencyId both as "value" (they travel as a both-or-neither pair); the
    // summary de-dupes so a paired change reads once, not "location, location" / "value, value".
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

    public async Task<PaginatedResponse<GetItemActivityResponse>?> Handle(
        GetItemActivityQuery request, CancellationToken ct)
    {
        // Both kinds: a consumable's feed is its notes plus its metadata audit rows (its movement
        // ledger lives on its own tab). Null only when the item is missing.
        var exists = await db.Items.AsNoTracking().AnyAsync(i => i.Id == request.Id, ct);
        if (!exists)
            return null;

        var filter = request.Filter?.Trim();
        // A text filter is over note text only, so it implies notes-only (activity.md §Filtering).
        var notesOnly = request.NotesOnly || !string.IsNullOrEmpty(filter);

        var auditRows = notesOnly ? [] : await LoadAuditRowsAsync(request.Id, ct);
        var noteRows = await LoadNoteRowsAsync(request.Id, filter, ct);

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
        // Only the item's own rows. Deliberately no ParentEntityId branch: the InventoryNote audit rows
        // (created/updated/deleted notes, parented to this item) are NOT projected into the feed — the
        // notes themselves are the feed rows, and a deleted note simply disappears (activity.md §Auditing notes).
        //
        // LEFT join Users (GroupJoin + SelectMany + DefaultIfEmpty), NOT an inner Join: an audit row
        // whose actor is null (a system action) or a since-deleted user must still surface. An inner
        // join silently drops those rows — the bug called out in the Documents audit doc §3.1.
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "Item" && a.EntityId == itemId)
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
                    Summary = GenerateSummary(x.Action, x.Changes, userName),
                };
                return (new SortKey(x.Timestamp, x.Timestamp, x.Id), row);
            })
            .ToList();
    }

    private async Task<List<(SortKey Key, GetItemActivityResponse Row)>> LoadNoteRowsAsync(
        Guid itemId, string? filter, CancellationToken ct)
    {
        var query = db.ItemNotes.AsNoTracking().Where(n => n.ItemId == itemId);

        if (!string.IsNullOrEmpty(filter))
        {
            // 3-arg ILike so a user-typed % or _ is matched literally (see LikeEscaper). Postgres-only:
            // InMemory can't translate it, so this branch is proven by the integration suite.
            var pattern = $"%{LikeEscaper.Escape(filter)}%";
            query = query.Where(n =>
                (n.Subject != null && EF.Functions.ILike(n.Subject, pattern, LikeEscaper.EscapeChar)) ||
                EF.Functions.ILike(n.Body, pattern, LikeEscaper.EscapeChar));
        }

        // Optional navs → LEFT JOINs; a note whose author was deleted (SetNull FK) still renders.
        var rows = await query
            .Select(n => new GetItemActivityResponse
            {
                Type = "note",
                Id = n.Id,
                Timestamp = n.OccurredAt,
                UserDisplayName = n.CreatedByUser != null ? n.CreatedByUser.DisplayName : null,
                UserEmail = n.CreatedByUser != null ? n.CreatedByUser.Email : null,
                Subject = n.Subject,
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

    private static string GenerateSummary(AuditAction action, string? changes, string userName) =>
        action switch
        {
            AuditAction.Created => $"{userName} added this item",
            AuditAction.Deleted => $"{userName} deleted this item",
            AuditAction.Updated => GenerateUpdatedSummary(changes, userName),
            AuditAction.SoftDeleted => $"{userName} moved this item to trash",
            AuditAction.Restored => $"{userName} restored this item",
            _ => $"{userName} modified this item",
        };

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
