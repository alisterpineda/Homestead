namespace Homestead.WebApi.Modules.Finances.PriceProviders.BankOfCanada;

// The "PriceSources:BankOfCanada" options bag. The Valet API needs no key, so enablement is the only
// thing an operator normally sets.
public class BankOfCanadaOptions() : PriceSourceOptions(DefaultBaseUrl)
{
    public const string DefaultBaseUrl = "https://www.bankofcanada.ca/valet/";
}
