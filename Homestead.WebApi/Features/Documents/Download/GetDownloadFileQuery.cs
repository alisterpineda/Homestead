using MediatR;

namespace Homestead.WebApi.Features.Documents.Download;

public record GetDownloadFileQuery(Guid JobId) : IRequest<IResult>;
