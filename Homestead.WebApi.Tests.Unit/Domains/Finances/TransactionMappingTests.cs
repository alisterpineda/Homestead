using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Transactions;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

/// <summary>
/// Regression guard for the NormalShape discriminator: Buy / Sell / Dividend are all 2-split
/// normal transactions, so before the discriminator they would have all read back as Transfer.
/// </summary>
public class TransactionMappingTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid OtherAccount = Guid.NewGuid();
    private static readonly Guid Cad = Guid.NewGuid();
    private static readonly Guid Veqt = Guid.NewGuid();
    private static readonly Guid Income = Guid.NewGuid();

    private static NormalTransaction Trade(NormalShape shape, decimal cashSign) => new()
    {
        Id = Guid.NewGuid(),
        OccurredAt = new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero),
        Memo = "",
        Type = TransactionType.Normal,
        NormalShape = shape,
        Splits =
        [
            new AccountTransactionSplit { Id = Guid.NewGuid(), AccountId = Account, CommodityId = Cad, Quantity = cashSign * 100m },
            new AccountTransactionSplit
            {
                Id = Guid.NewGuid(), AccountId = Account, CommodityId = Veqt, Quantity = -cashSign * 2m,
                UnitPriceRate = 50m, UnitPriceCommodityId = Cad,
            },
        ],
    };

    [Fact]
    public void Buy_MapsToInvestmentBuy_WithTradeDetails()
    {
        var response = TransactionMapping.ToResponse(Trade(NormalShape.InvestmentBuy, cashSign: -1m));

        Assert.Equal(TransactionShape.InvestmentBuy, response.Shape);
        var details = Assert.IsType<TradeDetails>(response.Details);
        Assert.Equal(Account, details.AccountId);
        Assert.Equal(Veqt, details.SecurityId);
        Assert.Equal(Cad, details.FundingCurrencyId);
        Assert.Equal(2m, details.Quantity);
        Assert.Equal(50m, details.UnitPrice);
    }

    [Fact]
    public void Sell_MapsToInvestmentSell_WithTradeDetails()
    {
        var response = TransactionMapping.ToResponse(Trade(NormalShape.InvestmentSell, cashSign: 1m));

        Assert.Equal(TransactionShape.InvestmentSell, response.Shape);
        var details = Assert.IsType<TradeDetails>(response.Details);
        Assert.Equal(2m, details.Quantity);
        Assert.Equal(50m, details.UnitPrice);
    }

    [Fact]
    public void Dividend_MapsToDividend_WithDividendDetails()
    {
        var dividend = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero),
            Memo = "Dividend",
            Type = TransactionType.Normal,
            NormalShape = NormalShape.Dividend,
            Splits =
            [
                new AccountTransactionSplit { Id = Guid.NewGuid(), AccountId = Account, CommodityId = Cad, Quantity = 12.50m },
                new AccountTransactionSplit { Id = Guid.NewGuid(), AccountId = Income, CommodityId = Cad, Quantity = -12.50m },
            ],
        };

        var response = TransactionMapping.ToResponse(dividend);

        Assert.Equal(TransactionShape.Dividend, response.Shape);
        var details = Assert.IsType<DividendDetails>(response.Details);
        Assert.Equal(Account, details.AccountId);
        Assert.Equal(Cad, details.PaymentCurrencyId);
        Assert.Equal(12.50m, details.Amount);
    }

    [Fact]
    public void Transfer_StillMapsToTransfer()
    {
        var transfer = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = new DateTimeOffset(2026, 4, 23, 0, 0, 0, TimeSpan.Zero),
            Memo = "",
            Type = TransactionType.Normal,
            NormalShape = NormalShape.Transfer,
            Splits =
            [
                new AccountTransactionSplit { Id = Guid.NewGuid(), AccountId = Account, CommodityId = Cad, Quantity = -100m },
                new AccountTransactionSplit { Id = Guid.NewGuid(), AccountId = OtherAccount, CommodityId = Cad, Quantity = 100m },
            ],
        };

        var response = TransactionMapping.ToResponse(transfer);

        Assert.Equal(TransactionShape.Transfer, response.Shape);
        Assert.IsType<TransferDetails>(response.Details);
    }
}
