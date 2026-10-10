using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.GetPayees;

public class GetPayeesHandler(AppDbContext db)
{
    public async Task<PaginatedResponse<PayeeResponse>> Handle(
        GetPayeesQuery request, CancellationToken ct)
    {
        var query = db.Payees.AsNoTracking();

        // Archived payees are hidden by default (archiving only hides from pickers/lists; old transactions
        // keep their payee). IncludeArchived surfaces them for the management page.
        if (request.IncludeArchived != true)
            query = query.Where(p => !p.IsArchived);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(search));
        }

        // TransactionCount is a correlated subquery (Payee has no reverse nav collection, by design), so the
        // projection lives inline here rather than in a static Expression.
        var projected = query.Select(p => new PayeeResponse
        {
            Id = p.Id,
            Name = p.Name,
            DefaultCategoryId = p.DefaultCategoryId,
            DefaultCategoryName = p.DefaultCategory != null ? p.DefaultCategory.Name : null,
            DefaultCategoryType = p.DefaultCategory != null ? (AccountType?)p.DefaultCategory.AccountType : null,
            Notes = p.Notes,
            IsArchived = p.IsArchived,
            TransactionCount = db.AccountTransactions.OfType<NormalTransaction>().Count(t => t.PayeeId == p.Id),
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
        });

        var sortBy = (request.SortBy ?? "name").Trim().ToLowerInvariant();
        var desc = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);

        // Name is the tiebreaker for the non-unique columns (TransactionCount/UpdatedAt tie constantly):
        // without it OFFSET/LIMIT pagination over an arbitrary tie order can skip or repeat rows between page
        // fetches. Name is unique (unique lower(Name) index), so it makes the order total. The Name branches
        // are already deterministic on their own.
        projected = (sortBy, desc) switch
        {
            ("transactions", false) => projected.OrderBy(p => p.TransactionCount).ThenBy(p => p.Name),
            ("transactions", true) => projected.OrderByDescending(p => p.TransactionCount).ThenBy(p => p.Name),
            ("updated", false) => projected.OrderBy(p => p.UpdatedAt).ThenBy(p => p.Name),
            ("updated", true) => projected.OrderByDescending(p => p.UpdatedAt).ThenBy(p => p.Name),
            (_, true) => projected.OrderByDescending(p => p.Name),
            _ => projected.OrderBy(p => p.Name),
        };

        return await projected.ToPaginatedAsync(request, ct);
    }
}
