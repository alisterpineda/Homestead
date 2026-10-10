namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.PatchTransfer;

public record PatchTransferCommand
{
    public Guid TransactionId { get; init; }
    public required Guid SourceAccountId { get; init; }
    public required Guid DestinationAccountId { get; init; }
    public required decimal SourceAmount { get; init; }
    public decimal? DestinationAmount { get; init; }
    public required DateOnly Date { get; init; }
    public string? Memo { get; init; }
}
