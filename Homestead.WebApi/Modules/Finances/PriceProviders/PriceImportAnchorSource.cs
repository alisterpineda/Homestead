namespace Homestead.WebApi.Modules.Finances.PriceProviders;

public enum PriceImportAnchorSource
{
    /// <summary>Caller supplied an explicit FromAt — treat as a deep backfill.</summary>
    ExplicitOverride,
    /// <summary>FromAt was inferred from max(PriceAt) of currently-held pairs.</summary>
    Watermark,
    /// <summary>No prior prices existed for this source — falling back to earliest transaction activity.</summary>
    EarliestActivity,
}
