using MediatR;

namespace Homestead.WebApi.Features.Documents.Export;

public record DownloadExportQuery(Guid JobId) : IRequest<IResult>;
