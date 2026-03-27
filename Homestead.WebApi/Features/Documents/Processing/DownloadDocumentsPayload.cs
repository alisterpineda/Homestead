namespace Homestead.WebApi.Features.Documents.Processing;

public record DownloadDocumentsPayload(List<Guid> DocumentIds);
