namespace Homestead.WebApi.Modules.Admin.Features.Backup.CreateBackupSource;

public record CreateBackupSourceResponse(Guid Id, string SourceKind, string TargetKey);
