using Homestead.WebApi.Modules.Finances.Features.Transactions;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.CreateTransfer;

public record CreateTransferCommand
{
    public required Guid SourceAccountId { get; init; }
    public required Guid DestinationAccountId { get; init; }
    public required decimal SourceAmount { get; init; }
    public decimal? DestinationAmount { get; init; }
    public required DateOnly Date { get; init; }
    public string? Memo { get; init; }
}
