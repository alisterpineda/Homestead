using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Download;

public record GetDownloadFileQuery(Guid JobId) : IRequest<IResult>;
