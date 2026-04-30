namespace Homestead.WebApi.Modules.Finances.PriceProviders;

public record PriceImportResult(IReadOnlyList<PriceObservation> Observations, string? Warning)
{
    public static PriceImportResult Success(IReadOnlyList<PriceObservation> observations) =>
        new(observations, null);

    public static PriceImportResult Partial(IReadOnlyList<PriceObservation> observations, string warning) =>
        new(observations, warning);
}
