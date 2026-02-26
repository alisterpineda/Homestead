namespace HomeSuiteHome.WebApi.Features.Documents.v1.CreateDocumentRevision;

public class CreateDocumentRevisionRequest
{
    public Guid DocumentId { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }
    public required IFormFile File { get; set; }
}
