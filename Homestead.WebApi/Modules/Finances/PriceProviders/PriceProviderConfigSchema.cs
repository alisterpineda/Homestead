namespace Homestead.WebApi.Modules.Finances.PriceProviders;

public enum PriceProviderConfigFieldType
{
    String,
    Boolean,
    Integer,
    Secret,
}

public record PriceProviderConfigField(
    string Key,
    string Label,
    PriceProviderConfigFieldType Type,
    bool IsRequired,
    string? Help = null,
    string? DefaultValue = null);

public record PriceProviderConfigSchema(IReadOnlyList<PriceProviderConfigField> Fields)
{
    public static readonly PriceProviderConfigSchema Empty = new(Array.Empty<PriceProviderConfigField>());
}
