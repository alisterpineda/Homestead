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
using Homestead.WebApi.Modules.Finances.Features.Categories.CreateCategory;
using Homestead.WebApi.Modules.Finances.Features.Categories.DeleteCategory;
using Homestead.WebApi.Modules.Finances.Features.Categories.GetCategories;
using Homestead.WebApi.Modules.Finances.Features.Categories.PatchCategory;
using Homestead.WebApi.Modules.Finances.Features.Commodities.CreateCommodity;
using Homestead.WebApi.Modules.Finances.Features.Commodities.DeleteCommodity;
using Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodities;
using Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodity;
using Homestead.WebApi.Modules.Finances.Features.Commodities.PatchCommodity;
using Homestead.WebApi.Modules.Finances.Features.CommodityPrices.CreateCommodityPrice;
using Homestead.WebApi.Modules.Finances.Features.CommodityPrices.DeleteCommodityPrices;
using Homestead.WebApi.Modules.Finances.Features.CommodityPrices.GetCommodityPrices;
using Homestead.WebApi.Modules.Finances.Features.CommodityPrices.PatchCommodityPrice;
using Homestead.WebApi.Modules.Finances.Features.Imports.CommitStatementImport;
using Homestead.WebApi.Modules.Finances.Features.Imports.GetStatementImport;
using Homestead.WebApi.Modules.Finances.Features.Imports.InitiateStatementImport;
using Homestead.WebApi.Modules.Finances.Features.Imports.RollbackStatementImport;
using Homestead.WebApi.Modules.Finances.Features.OpeningBalances.CreateOpeningBalance;
using Homestead.WebApi.Modules.Finances.Features.OpeningBalances.DeleteOpeningBalance;
using Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalance;
using Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalances;
using Homestead.WebApi.Modules.Finances.Features.OpeningBalances.PatchOpeningBalance;
using Homestead.WebApi.Modules.Finances.Features.Payees.CreatePayee;
using Homestead.WebApi.Modules.Finances.Features.Payees.DeletePayee;
using Homestead.WebApi.Modules.Finances.Features.Payees.DeletePayeeMapping;
using Homestead.WebApi.Modules.Finances.Features.Payees.GetPayeeMappings;
using Homestead.WebApi.Modules.Finances.Features.Payees.GetPayees;
using Homestead.WebApi.Modules.Finances.Features.Payees.MergePayee;
using Homestead.WebApi.Modules.Finances.Features.Payees.PatchPayee;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.DeletePriceSourceSchedule;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSource;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSources;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.PatchPriceSource;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.RunPriceSourceImportNow;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.UpsertPriceSourceSchedule;
using Homestead.WebApi.Modules.Finances.Features.Reports.GetNetWorthHistory;
using Homestead.WebApi.Modules.Finances.Features.Settings.GetFinanceSettings;
using Homestead.WebApi.Modules.Finances.Features.Settings.PatchFinanceSettings;
using Homestead.WebApi.Modules.Finances.Features.Transactions.BulkDeleteTransactions;
using Homestead.WebApi.Modules.Finances.Features.Transactions.DeleteTransaction;
using Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.AddTransactionDocumentLink;
using Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.GetTransactionDocuments;
using Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.RemoveTransactionDocumentLink;
using Homestead.WebApi.Modules.Finances.Features.Transactions.GetAccountTransactions;
using Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransaction;
using Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransactions;
using Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.CreateTransfer;
using Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.PatchTransfer;
using Homestead.WebApi.Modules.Finances.Features.Transactions.Trades.CreateTrade;
using Homestead.WebApi.Modules.Finances.Features.Transactions.Trades.PatchTrade;
using Homestead.WebApi.Modules.Finances.Features.Transactions.Dividends.CreateDividend;
using Homestead.WebApi.Modules.Finances.Features.Transactions.Dividends.PatchDividend;
using Homestead.WebApi.Modules.Finances.Features.Transactions.Flows.CreateFlow;
using Homestead.WebApi.Modules.Finances.Features.Transactions.Flows.PatchFlow;
using Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.CreateDrip;
using Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.PatchDrip;
using Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.DeleteDrip;

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

        // Income/expense categories — Account rows of type Income/Expense, managed separately from
        // balance-sheet accounts. Mirrors the commodities group.
        var categories = v1.MapGroup("/categories");
        GetCategoriesEndpoint.Map(categories);
        CreateCategoryEndpoint.Map(categories);
        PatchCategoryEndpoint.Map(categories);
        DeleteCategoryEndpoint.Map(categories);

        // Household payees (counterparties for flows). Global lookup, mirrors the categories group.
        var payees = v1.MapGroup("/payees");
        GetPayeesEndpoint.Map(payees);
        CreatePayeeEndpoint.Map(payees);
        PatchPayeeEndpoint.Map(payees);
        DeletePayeeEndpoint.Map(payees);
        MergePayeeEndpoint.Map(payees);
        // Learned raw-description → payee mappings, surfaced on the payee edit dialog.
        GetPayeeMappingsEndpoint.Map(payees);

        // Learned mappings addressed by their own id (prune a wrong/stale fingerprint).
        var payeeMappings = v1.MapGroup("/payee-mappings");
        DeletePayeeMappingEndpoint.Map(payeeMappings);

        var prices = v1.MapGroup("/commodity-prices");
        GetCommodityPricesEndpoint.Map(prices);
        CreateCommodityPriceEndpoint.Map(prices);
        PatchCommodityPriceEndpoint.Map(prices);
        DeleteCommodityPricesEndpoint.Map(prices);

        // Generic transactions surface (read + delete).
        var transactions = v1.MapGroup("/transactions");
        GetTransactionsEndpoint.Map(transactions);
        GetTransactionEndpoint.Map(transactions);
        DeleteTransactionEndpoint.Map(transactions);
        BulkDeleteTransactionsEndpoint.Map(transactions);

        // Document links (receipts/invoices/statements) on any transaction shape.
        GetTransactionDocumentsEndpoint.Map(transactions);
        AddTransactionDocumentLinkEndpoint.Map(transactions);
        RemoveTransactionDocumentLinkEndpoint.Map(transactions);

        // Per-shape transaction create/edit endpoints under /transactions/{shape}.
        var transfers = transactions.MapGroup("/transfers");
        CreateTransferEndpoint.Map(transfers);
        PatchTransferEndpoint.Map(transfers);

        var trades = transactions.MapGroup("/trades");
        CreateTradeEndpoint.Map(trades);
        PatchTradeEndpoint.Map(trades);

        var dividends = transactions.MapGroup("/dividends");
        CreateDividendEndpoint.Map(dividends);
        PatchDividendEndpoint.Map(dividends);

        // Ordinary spend/income flows (Expense/Income) — direction carried in the request body.
        var flows = transactions.MapGroup("/flows");
        CreateFlowEndpoint.Map(flows);
        PatchFlowEndpoint.Map(flows);

        // DRIP (dividend reinvestment) writes a linked Dividend + Buy pair. The both-halves delete
        // lives here; the single-half delete stays on the generic /transactions delete.
        var drips = transactions.MapGroup("/drips");
        CreateDripEndpoint.Map(drips);
        PatchDripEndpoint.Map(drips);
        DeleteDripEndpoint.Map(drips);

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

        // Statement parsing → finance import spine (upload/parse → review → commit → rollback).
        var imports = v1.MapGroup("/imports");
        InitiateStatementImportEndpoint.Map(imports);
        GetStatementImportEndpoint.Map(imports);
        CommitStatementImportEndpoint.Map(imports);
        RollbackStatementImportEndpoint.Map(imports);

        return app;
    }
}
