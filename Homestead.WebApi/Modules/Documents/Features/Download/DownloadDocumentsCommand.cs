using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Download;

public record DownloadDocumentsCommand(List<Guid>? Ids) : IRequest<DownloadDocumentsResponse>;
