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

    // Beancount sign flip applied when storing user-positive input as a split quantity:
    // a positive "owed" input on a liability account writes as a negative stored quantity
    // (more owed = more negative). Used for opening balances and balance-entry assertions;
    // transfers don't need this because flow direction (-amount/+amount) already does it.
    public static decimal ApplySign(this AccountType type, decimal amount) =>
        type == AccountType.Liability ? -amount : amount;
}
