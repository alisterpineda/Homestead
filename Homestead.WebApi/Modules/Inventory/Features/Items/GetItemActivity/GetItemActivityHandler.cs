using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItemActivity;

public class GetItemActivityHandler(AppDbContext db)
    : IRequestHandler<GetItemActivityQuery, PaginatedResponse<GetItemActivityResponse>?>
{
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
        ["IsContainer"] = "container role",
        ["Notes"] = "notes",
        ["Unit"] = "unit",
        ["RestockThreshold"] = "restock threshold",
    };

    public async Task<PaginatedResponse<GetItemActivityResponse>?> Handle(
        GetItemActivityQuery request, CancellationToken ct)
    {
        var exists = await db.Items.AsNoTracking()
            .AnyAsync(i => i.Id == request.Id && i.Fungibility == Fungibility.NonFungible, ct);
        if (!exists)
            return null;

        // LEFT join Users (GroupJoin + SelectMany + DefaultIfEmpty), NOT an inner Join: an audit row
        // whose actor is null (a system action) or a since-deleted user must still surface. An inner
        // join silently drops those rows — the bug called out in the Documents audit doc §3.1.
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "Item" && a.EntityId == request.Id)
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

        // Summaries parse the Changes JSON, so build + order + paginate in memory.
        var all = rows
            .Select(x =>
            {
                var userName = x.DisplayName ?? x.Email ?? "Someone";
                return new GetItemActivityResponse
                {
                    Id = x.Id,
                    Timestamp = x.Timestamp,
                    UserDisplayName = x.DisplayName,
                    UserEmail = x.Email,
                    Summary = GenerateSummary(x.Action, x.Changes, userName),
                };
            })
            // Id is the tiebreaker (mirroring GetSupplyMovements) so equal-timestamp rows — likely on
            // coarse-resolution clocks — get a stable order and can't drift across page boundaries.
            .OrderByDescending(x => x.Timestamp)
            .ThenByDescending(x => x.Id)
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
