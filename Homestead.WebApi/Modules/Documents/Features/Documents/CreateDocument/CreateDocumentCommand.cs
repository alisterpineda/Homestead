using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.CreateDocument;

public class CreateDocumentCommand : IRequest<CreateDocumentResponse>
{
    public string? Name { get; set; }
    public string? Label { get; set; }
    public Guid? GroupId { get; set; }
    public required IFormFile File { get; set; }
}
