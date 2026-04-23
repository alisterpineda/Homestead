namespace Homestead.WebApi.Domain.Finances;

public static class AccountKindExtensions
{
    public static AccountType ToAccountType(this AccountKind kind) => kind switch
    {
        AccountKind.Cash => AccountType.Asset,
        AccountKind.Investment => AccountType.Asset,
        AccountKind.OtherAsset => AccountType.Asset,
        AccountKind.CreditCard => AccountType.Liability,
        AccountKind.Loan => AccountType.Liability,
        AccountKind.OtherLiability => AccountType.Liability,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"Unsupported account kind: {kind}"),
    };
}
