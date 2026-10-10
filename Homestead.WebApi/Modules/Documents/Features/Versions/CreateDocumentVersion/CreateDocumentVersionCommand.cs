namespace Homestead.WebApi.Modules.Documents.Features.Versions.CreateDocumentVersion;

public class CreateDocumentVersionCommand
{
    public Guid DocumentId { get; set; }
    public string? Label { get; set; }
    public required IFormFile File { get; set; }
}
