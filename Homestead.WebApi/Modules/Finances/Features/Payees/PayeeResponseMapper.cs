using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Payees;

internal static class PayeeResponseMapper
{
    /// <summary>
    /// In-memory Payee → PayeeResponse for the write paths (create/patch — no reload needed). The list read
    /// handler projects inline instead, because <see cref="PayeeResponse.TransactionCount"/> is a correlated
    /// subquery a static expression can't reference (Payee has no reverse nav). <paramref name="defaultCategory"/>
    /// is the validated default-category row (null when the payee has no default category).
    /// </summary>
    public static PayeeResponse ToResponse(Payee p, int transactionCount, Account? defaultCategory) => new()
    {
        Id = p.Id,
        Name = p.Name,
        DefaultCategoryId = p.DefaultCategoryId,
        DefaultCategoryName = defaultCategory?.Name,
        DefaultCategoryType = defaultCategory?.AccountType,
        Notes = p.Notes,
        IsArchived = p.IsArchived,
        TransactionCount = transactionCount,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
    };
}
