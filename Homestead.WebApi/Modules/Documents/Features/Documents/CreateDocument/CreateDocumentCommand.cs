namespace Homestead.WebApi.Modules.Documents.Features.Documents.CreateDocument;

public class CreateDocumentCommand
{
    public string? Name { get; set; }
    public string? Label { get; set; }
    public Guid? GroupId { get; set; }
    public required IFormFile File { get; set; }
}
