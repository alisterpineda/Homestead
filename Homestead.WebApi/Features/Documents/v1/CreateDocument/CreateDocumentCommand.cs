using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.CreateDocument;

public class CreateDocumentCommand : IRequest<CreateDocumentResponse>
{
    public string? Name { get; set; }
    public string? Label { get; set; }
    public Guid? GroupId { get; set; }
    public required IFormFile File { get; set; }
}
