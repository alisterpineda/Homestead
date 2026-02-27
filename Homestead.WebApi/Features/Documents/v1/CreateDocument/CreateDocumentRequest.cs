namespace Homestead.WebApi.Features.Documents.v1.CreateDocument;

public class CreateDocumentRequest
{
    public string? Name { get; set; }
    public string? Label { get; set; }
    public required IFormFile File { get; set; }
}
