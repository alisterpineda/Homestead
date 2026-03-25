using MediatR;

namespace Homestead.WebApi.Features.Documents.Documents.BulkUpdateDocuments;

public record BulkUpdateDocumentsCommand(
    List<Guid> Ids,
    List<string>? TagPaths,
    List<Guid>? PartyIds,
    List<string>? RemoveTagPaths,
    List<Guid>? RemovePartyIds) : IRequest<BulkUpdateDocumentsResponse>;
