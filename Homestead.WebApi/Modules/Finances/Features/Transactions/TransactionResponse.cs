using System.Text.Json.Serialization;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions;

/// <summary>
/// Discriminator for the user-facing transaction shape — the lens the UI uses to render and
/// edit a stored transaction. Distinct from the storage-level <c>TransactionType</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TransactionShape
{
    Transfer,
    OpeningBalance,
    BalanceEntry,
    InvestmentBuy,
    InvestmentSell,
    Dividend,
    Expense,
    Income,
    Other,
}

public record TransactionSplitResponse
{
    public required Guid Id { get; init; }
    public required Guid AccountId { get; init; }
    public required Guid CommodityId { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? UnitPriceRate { get; init; }
    public Guid? UnitPriceCommodityId { get; init; }
    public string? Memo { get; init; }
}

public record TransactionResponse
{
    public required Guid Id { get; init; }
    public required TransactionShape Shape { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string Memo { get; init; }
    public Guid? RelatedTransactionId { get; init; }
    public required IReadOnlyList<TransactionSplitResponse> Splits { get; init; }

    /// <summary>Number of linked documents (any origin, including trashed/inaccessible ones).</summary>
    public required int DocumentLinkCount { get; init; }

    /// <summary>
    /// Shape-specific fields. Populated only for shapes that need additional metadata beyond the
    /// generic transaction header (e.g., balance entries surface AssertedQuantity here).
    /// </summary>
    public TransactionDetails? Details { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BalanceEntryDetails), "balanceEntry")]
[JsonDerivedType(typeof(TransferDetails), "transfer")]
[JsonDerivedType(typeof(TradeDetails), "trade")]
[JsonDerivedType(typeof(DividendDetails), "dividend")]
[JsonDerivedType(typeof(FlowDetails), "flow")]
public abstract record TransactionDetails;

public record TransferDetails(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    Guid SourceCurrencyId,
    Guid DestinationCurrencyId,
    decimal SourceAmount,
    decimal DestinationAmount) : TransactionDetails;

public record BalanceEntryDetails(
    Guid AccountId,
    Guid AssertedCommodityId,
    decimal AssertedAmount) : TransactionDetails;

/// <summary>
/// Shape details for an investment Buy or Sell. Buy-vs-Sell is conveyed by the response
/// <see cref="TransactionResponse.Shape"/> (<see cref="TransactionShape.InvestmentBuy"/> /
/// <see cref="TransactionShape.InvestmentSell"/>), so no side field is needed here.
/// All fields are reconstructed from the annotated (security) leg.
/// </summary>
public record TradeDetails(
    Guid AccountId,
    Guid SecurityId,
    Guid FundingCurrencyId,
    decimal Quantity,
    decimal UnitPrice) : TransactionDetails;

public record DividendDetails(
    Guid AccountId,
    Guid PaymentCurrencyId,
    decimal Amount) : TransactionDetails;

/// <summary>
/// Shape details for an ordinary spend/income flow (Expense or Income). Expense-vs-Income is conveyed
/// by the response <see cref="TransactionResponse.Shape"/>, so no direction field is needed here.
/// <see cref="Amount"/> is the positive magnitude of the user-account leg; <see cref="CurrencyId"/> is
/// the account's reporting currency (both legs share it).
///
/// The category triple describes the counterpart leg: a flow posted to a user category carries its id,
/// leaf <see cref="CategoryName"/>, and full breadcrumb <see cref="CategoryPath"/>. A flow left on the
/// system Uncategorised bucket leaves all three null.
/// </summary>
public record FlowDetails(
    Guid AccountId,
    Guid CurrencyId,
    decimal Amount,
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryPath) : TransactionDetails;
