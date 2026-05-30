using MediatR;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkUpdateDocuments;

public record BulkUpdateDocumentsCommand(
    List<Guid> Ids,
    List<string>? TagPaths,
    List<Guid>? PartyIds,
    List<string>? RemoveTagPaths,
    List<Guid>? RemovePartyIds) : ICommand<BulkUpdateDocumentsResponse>;
