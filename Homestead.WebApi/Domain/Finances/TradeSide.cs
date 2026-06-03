namespace Homestead.WebApi.Domain.Finances;

/// <summary>
/// Direction of an investment trade. Buy moves cash out of the investment account and units in;
/// Sell is the mirror. Maps to <see cref="NormalShape.InvestmentBuy"/> / <see cref="NormalShape.InvestmentSell"/>.
/// </summary>
public enum TradeSide
{
    Buy,
    Sell,
}
