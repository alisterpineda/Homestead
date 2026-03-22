using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.PermanentlyDeleteDocuments;

public record PermanentlyDeleteDocumentsRequest(List<Guid> Ids) : IRequest<PermanentlyDeleteDocumentsResponse>;
