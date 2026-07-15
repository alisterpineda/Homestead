using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts;

public record AccountResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required AccountType AccountType { get; init; }
    public AccountKind? Kind { get; init; }
    public required Guid ReportingCurrencyId { get; init; }
    public string? Institution { get; init; }

    // Full account number — populated only on owner-facing detail/edit responses. Omitted (null) on
    // list/summary responses, which carry AccountNumberMasked instead.
    public string? AccountNumber { get; init; }

    // Last-4 masked form (e.g. ••••1234), derived server-side. Always present when a number is stored.
    public string? AccountNumberMasked { get; init; }

    public required bool IsSystem { get; init; }
    public required bool IsArchived { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    public decimal? DisplayValue { get; init; }
    public decimal? NativeBalance { get; init; }
    public int UnpricedHoldingCount { get; init; }
    public required bool HasTransactions { get; init; }
    public DateTimeOffset? LatestTransactionDate { get; init; }
}
