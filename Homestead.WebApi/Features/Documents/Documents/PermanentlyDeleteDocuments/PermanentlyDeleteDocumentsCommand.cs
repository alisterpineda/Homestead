using MediatR;

namespace Homestead.WebApi.Features.Documents.Documents.PermanentlyDeleteDocuments;

public record PermanentlyDeleteDocumentsCommand(List<Guid> Ids) : IRequest<PermanentlyDeleteDocumentsResponse>;
