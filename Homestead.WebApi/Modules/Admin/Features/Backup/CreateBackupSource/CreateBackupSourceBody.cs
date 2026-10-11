namespace Homestead.WebApi.Modules.Admin.Features.Backup.CreateBackupSource;

public record CreateBackupSourceBody
{
    /// <summary>An <c>IBackupSourceDescriptor.Key</c> from the catalog.</summary>
    public required string SourceKind { get; init; }

    /// <summary>A <c>Backup:Targets:{key}</c>. Persisted in the resolver's canonical casing.</summary>
    public required string TargetKey { get; init; }
}
