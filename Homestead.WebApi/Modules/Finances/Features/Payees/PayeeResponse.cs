using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Payees;

/// <summary>
/// A household payee (counterparty for flows), as returned by the picker's server-search and the Payees
/// management page. Carries identity, the optional default category (name + type for direction-aware
/// client-side autofill), management-surface fields, and how many transactions reference it (computed at
/// query time — <see cref="Domain.Finances.Payee"/> has no reverse nav).
/// </summary>
public record PayeeResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid? DefaultCategoryId { get; init; }
    public string? DefaultCategoryName { get; init; }

    /// <summary>Type of the default category (Income/Expense). Guards direction-aware autofill on the flow sheet.</summary>
    public AccountType? DefaultCategoryType { get; init; }

    public string? Notes { get; init; }
    public required bool IsArchived { get; init; }

    /// <summary>Number of transactions attributed to this payee. Gates delete in the UI.</summary>
    public required int TransactionCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
