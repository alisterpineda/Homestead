using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.PermanentlyDeleteDocuments;

public record PermanentlyDeleteDocumentsCommand(List<Guid> Ids) : IRequest<PermanentlyDeleteDocumentsResponse>;
