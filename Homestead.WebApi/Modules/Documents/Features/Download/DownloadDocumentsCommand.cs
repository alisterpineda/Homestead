using MediatR;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Download;

public record DownloadDocumentsCommand(List<Guid>? Ids) : ICommand<DownloadDocumentsResponse>;
