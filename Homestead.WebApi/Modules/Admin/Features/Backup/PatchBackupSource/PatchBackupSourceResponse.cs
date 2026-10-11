namespace Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupSource;

public record PatchBackupSourceResponse(Guid Id, bool IsEnabled, DateTimeOffset? PruneBlockedAt);
