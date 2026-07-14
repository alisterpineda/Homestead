namespace Homestead.WebApi.Domain.Finances;

/// <summary>
/// Second discriminator (independent of <see cref="TransactionType"/>) carried by every
/// <see cref="NormalTransaction"/>. Buy, Sell, and Dividend are all 2-split normal transactions,
/// so structural inference cannot tell them apart from a Transfer — this column is the stable,
/// join-free discriminator the mapping reads back. NULL on non-normal TPH rows (OB/BE).
/// </summary>
public enum NormalShape
{
    Transfer,
    InvestmentBuy,
    InvestmentSell,
    Dividend,
    Expense,
    Income,
}
