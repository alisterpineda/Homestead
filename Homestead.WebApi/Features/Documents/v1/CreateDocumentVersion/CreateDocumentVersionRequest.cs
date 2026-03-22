using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.CreateDocumentVersion;

public class CreateDocumentVersionRequest : IRequest<CreateDocumentVersionResponse>
{
    public Guid DocumentId { get; set; }
    public string? Label { get; set; }
    public required IFormFile File { get; set; }
}
