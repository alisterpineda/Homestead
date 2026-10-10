namespace Homestead.WebApi.Modules.Documents.Features.Download;

public record DownloadDocumentsCommand(List<Guid>? Ids);
