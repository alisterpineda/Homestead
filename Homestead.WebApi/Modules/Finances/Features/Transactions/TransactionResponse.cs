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
    StockSplit,
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
    public required string Description { get; init; }
    public Guid? RelatedTransactionId { get; init; }
    public required IReadOnlyList<TransactionSplitResponse> Splits { get; init; }

    /// <summary>
    /// Shape-specific fields. Populated only for shapes that need additional metadata beyond the
    /// generic transaction header (e.g., balance entries surface AssertedQuantity here).
    /// </summary>
    public TransactionDetails? Details { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BalanceEntryDetails), "balanceEntry")]
[JsonDerivedType(typeof(TransferDetails), "transfer")]
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
    decimal AssertedAmount,
    string? Note) : TransactionDetails;
