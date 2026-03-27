using MediatR;

namespace Homestead.WebApi.Features.Documents.Download;

public record DownloadDocumentsCommand(List<Guid>? Ids) : IRequest<DownloadDocumentsResponse>;
