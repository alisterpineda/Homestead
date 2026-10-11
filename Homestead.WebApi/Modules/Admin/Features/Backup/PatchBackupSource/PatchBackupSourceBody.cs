namespace Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupSource;

public record PatchBackupSourceBody
{
    public bool? IsEnabled { get; init; }

    /// <summary>
    /// Clears the bulk-delete breaker latch. An EXPLICIT flag rather than a side effect of any PATCH:
    /// the latch banner is the only place a blocked prune is reported, so toggling the pairing off and on
    /// again must not silently dismiss it.
    /// </summary>
    public bool? AcknowledgePruneBlock { get; init; }
}
