using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.PermanentlyDeleteDocuments;

public record PermanentlyDeleteDocumentsCommand(List<Guid> Ids) : IRequest<PermanentlyDeleteDocumentsResponse>;
