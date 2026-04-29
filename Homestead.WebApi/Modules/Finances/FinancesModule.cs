using Asp.Versioning.Builder;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Finances.Features.Accounts.CreateAccount;
using Homestead.WebApi.Modules.Finances.Features.Accounts.DeleteAccount;
using Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccount;
using Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccounts;
using Homestead.WebApi.Modules.Finances.Features.Accounts.GetPositions;
using Homestead.WebApi.Modules.Finances.Features.Accounts.PatchAccount;
using Homestead.WebApi.Modules.Finances.Features.BalanceEntries.CreateBalanceEntry;
using Homestead.WebApi.Modules.Finances.Features.BalanceEntries.GetBalanceEntries;
using Homestead.WebApi.Modules.Finances.Features.BalanceEntries.GetBalanceEntry;
using Homestead.WebApi.Modules.Finances.Features.BalanceEntries.PatchBalanceEntry;
using Homestead.WebApi.Modules.Finances.Features.Commodities.CreateCommodity;
using Homestead.WebApi.Modules.Finances.Features.Commodities.DeleteCommodity;
using Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodities;
using Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodity;
using Homestead.WebApi.Modules.Finances.Features.Commodities.PatchCommodity;
using Homestead.WebApi.Modules.Finances.Features.CommodityPrices.CreateCommodityPrice;
using Homestead.WebApi.Modules.Finances.Features.CommodityPrices.DeleteCommodityPrice;
using Homestead.WebApi.Modules.Finances.Features.CommodityPrices.GetCommodityPrices;
using Homestead.WebApi.Modules.Finances.Features.CommodityPrices.PatchCommodityPrice;
using Homestead.WebApi.Modules.Finances.Features.OpeningBalances.CreateOpeningBalance;
using Homestead.WebApi.Modules.Finances.Features.OpeningBalances.DeleteOpeningBalance;
using Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalance;
using Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalances;
using Homestead.WebApi.Modules.Finances.Features.OpeningBalances.PatchOpeningBalance;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.DeletePriceSourceSchedule;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSource;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSources;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.PatchPriceSource;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.RunPriceSourceImportNow;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.UpsertPriceSourceSchedule;
using Homestead.WebApi.Modules.Finances.Features.Reports.GetNetWorthHistory;
using Homestead.WebApi.Modules.Finances.Features.Settings.GetFinanceSettings;
using Homestead.WebApi.Modules.Finances.Features.Settings.PatchFinanceSettings;
using Homestead.WebApi.Modules.Finances.Features.Transactions.DeleteTransaction;
using Homestead.WebApi.Modules.Finances.Features.Transactions.GetAccountTransactions;
using Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransaction;
using Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransactions;
using Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.CreateTransfer;
using Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.PatchTransfer;

namespace Homestead.WebApi.Modules.Finances;

public static class FinancesModule
{
    public static IVersionedEndpointRouteBuilder MapFinancesEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/finances")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Finances")
            .RequireAuthorization(AuthPolicies.ActiveUser);

        var accounts = v1.MapGroup("/accounts");
        GetAccountsEndpoint.Map(accounts);
        GetAccountEndpoint.Map(accounts);
        CreateAccountEndpoint.Map(accounts);
        PatchAccountEndpoint.Map(accounts);
        DeleteAccountEndpoint.Map(accounts);

        // Opening balances are sub-resources of accounts.
        CreateOpeningBalanceEndpoint.Map(accounts);
        GetOpeningBalancesEndpoint.Map(accounts);
        GetOpeningBalanceEndpoint.Map(accounts);
        PatchOpeningBalanceEndpoint.Map(accounts);
        DeleteOpeningBalanceEndpoint.Map(accounts);

        // Balance entries are sub-resources of accounts.
        CreateBalanceEntryEndpoint.Map(accounts);
        GetBalanceEntriesEndpoint.Map(accounts);
        GetBalanceEntryEndpoint.Map(accounts);
        PatchBalanceEntryEndpoint.Map(accounts);

        // Per-account transaction list (Activity tab).
        GetAccountTransactionsEndpoint.Map(accounts);

        // Per-account positions (Investment accounts only).
        GetPositionsEndpoint.Map(accounts);

        var commodities = v1.MapGroup("/commodities");
        GetCommoditiesEndpoint.Map(commodities);
        GetCommodityEndpoint.Map(commodities);
        CreateCommodityEndpoint.Map(commodities);
        PatchCommodityEndpoint.Map(commodities);
        DeleteCommodityEndpoint.Map(commodities);

        var prices = v1.MapGroup("/commodity-prices");
        GetCommodityPricesEndpoint.Map(prices);
        CreateCommodityPriceEndpoint.Map(prices);
        PatchCommodityPriceEndpoint.Map(prices);
        DeleteCommodityPriceEndpoint.Map(prices);

        // Generic transactions surface (read + delete).
        var transactions = v1.MapGroup("/transactions");
        GetTransactionsEndpoint.Map(transactions);
        GetTransactionEndpoint.Map(transactions);
        DeleteTransactionEndpoint.Map(transactions);

        // Per-shape transaction create/edit endpoints under /transactions/{shape}.
        var transfers = transactions.MapGroup("/transfers");
        CreateTransferEndpoint.Map(transfers);
        PatchTransferEndpoint.Map(transfers);

        var settings = v1.MapGroup("/settings");
        GetFinanceSettingsEndpoint.Map(settings);
        PatchFinanceSettingsEndpoint.Map(settings);

        var priceSources = v1.MapGroup("/settings/price-sources")
            .RequireAuthorization(AuthPolicies.RequireAdmin);
        GetPriceSourcesEndpoint.Map(priceSources);
        GetPriceSourceEndpoint.Map(priceSources);
        PatchPriceSourceEndpoint.Map(priceSources);
        UpsertPriceSourceScheduleEndpoint.Map(priceSources);
        DeletePriceSourceScheduleEndpoint.Map(priceSources);
        RunPriceSourceImportNowEndpoint.Map(priceSources);

        var reports = v1.MapGroup("/reports");
        GetNetWorthHistoryEndpoint.Map(reports);

        return app;
    }
}
