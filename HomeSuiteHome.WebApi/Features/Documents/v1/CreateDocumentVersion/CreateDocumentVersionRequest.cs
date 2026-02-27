namespace HomeSuiteHome.WebApi.Features.Documents.v1.CreateDocumentVersion;

public class CreateDocumentVersionRequest
{
    public Guid DocumentId { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }
    public required IFormFile File { get; set; }
}
