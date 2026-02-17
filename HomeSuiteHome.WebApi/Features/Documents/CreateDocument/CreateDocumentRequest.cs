namespace HomeSuiteHome.WebApi.Features.Documents.CreateDocument;

public class CreateDocumentRequest
{
    public string? Name { get; set; }
    public required IFormFile File { get; set; }
}
