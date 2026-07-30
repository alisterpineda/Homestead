using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.GetDocumentBackupState;

/// <summary>Where one document stands in every mirror that is supposed to be carrying it.</summary>
public record GetDocumentBackupStateQuery(Guid DocumentId)
    : IRequest<GetDocumentBackupStateResponse?>;
