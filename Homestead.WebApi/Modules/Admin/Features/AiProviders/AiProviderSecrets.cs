namespace Homestead.WebApi.Modules.Admin.Features.AiProviders;

// JSON config keys that hold secrets and must be masked in responses / merged from the stored
// value on update. Mirrors the secret-field handling in Modules/Finances/Features/PriceSources.
public static class AiProviderSecrets
{
    public static readonly string[] Keys = ["apiKey"];
}
