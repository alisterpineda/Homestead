namespace Homestead.WebApi.Modules.Documents.Processing;

public record DownloadDocumentsPayload(List<Guid> DocumentIds);
