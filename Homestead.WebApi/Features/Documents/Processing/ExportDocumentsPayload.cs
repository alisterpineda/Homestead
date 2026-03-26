namespace Homestead.WebApi.Features.Documents.Processing;

public record ExportDocumentsPayload(List<Guid> DocumentIds);
