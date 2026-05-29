using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domain.Documents;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.ClassifyDocument;

public record ClassifyPreviewQuery : IRequest<ClassifyPreviewResponse>, IRequireDocumentAuthorization
{
    public Guid DocumentId { get; init; }
    public Guid? AiProviderId { get; init; }

    /// <summary>
    /// The fields to classify. Null or empty defaults to all classify fields (Tags, Parties, Name).
    /// Summary is never part of the classify scope — use the separate summarize action instead.
    /// </summary>
    public List<DocumentSuggestionField>? Fields { get; init; }

    Guid IRequireDocumentAuthorization.DocumentId => DocumentId;
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
