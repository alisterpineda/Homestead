using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.GetBackupTargets;

public record GetBackupTargetsQuery : IRequest<GetBackupTargetsResponse>;
