using Asp.Versioning.Builder;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Finances.Features.Accounts.CreateAccount;
using Homestead.WebApi.Modules.Finances.Features.Accounts.DeleteAccount;
using Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccount;
using Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccounts;
using Homestead.WebApi.Modules.Finances.Features.Accounts.PatchAccount;
using Homestead.WebApi.Modules.Finances.Features.Commodities.CreateCommodity;
using Homestead.WebApi.Modules.Finances.Features.Commodities.DeleteCommodity;
using Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodities;
using Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodity;
using Homestead.WebApi.Modules.Finances.Features.Commodities.PatchCommodity;
using Homestead.WebApi.Modules.Finances.Features.CommodityPrices.CreateCommodityPrice;
using Homestead.WebApi.Modules.Finances.Features.CommodityPrices.DeleteCommodityPrice;
using Homestead.WebApi.Modules.Finances.Features.CommodityPrices.GetCommodityPrices;
using Homestead.WebApi.Modules.Finances.Features.CommodityPrices.PatchCommodityPrice;
using Homestead.WebApi.Modules.Finances.Features.Settings.GetFinanceSettings;
using Homestead.WebApi.Modules.Finances.Features.Settings.PatchFinanceSettings;

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

        var settings = v1.MapGroup("/settings");
        GetFinanceSettingsEndpoint.Map(settings);
        PatchFinanceSettingsEndpoint.Map(settings);

        return app;
    }
}
