using Homestead.WebApi.Domain.Finances;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Modules.Finances.PriceProviders;

// Scoped rather than singleton because providers are keyed SCOPED services over typed HttpClients.
public sealed class PriceSourceRegistry(IServiceProvider serviceProvider) : IPriceSourceRegistry
{
    public IPriceProvider? Get(CommodityPriceSource source) =>
        source.Kind() == CommodityPriceSourceKind.External
            ? serviceProvider.GetKeyedService<IPriceProvider>(source)
            : null;

    public bool IsEnabled(CommodityPriceSource source) =>
        source.Kind() != CommodityPriceSourceKind.External || (Get(source)?.IsEnabled ?? false);

    public bool IsConfigured(CommodityPriceSource source) =>
        source.Kind() != CommodityPriceSourceKind.External || (Get(source)?.IsConfigured ?? false);

    public IReadOnlySet<CommodityPriceSource> EnabledSources
    {
        get
        {
            var enabled = new HashSet<CommodityPriceSource>();
            foreach (var source in Enum.GetValues<CommodityPriceSource>())
            {
                if (IsEnabled(source)) enabled.Add(source);
            }
            return enabled;
        }
    }
}
