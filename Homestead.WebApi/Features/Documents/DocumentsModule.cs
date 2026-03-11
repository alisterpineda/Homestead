using Asp.Versioning.Builder;
using Homestead.WebApi.Features.Documents.v1.BulkDeleteDocuments;
using Homestead.WebApi.Features.Documents.v1.BulkDeleteParties;
using Homestead.WebApi.Features.Documents.v1.BulkUpdateDocuments;
using Homestead.WebApi.Features.Documents.v1.BulkDeleteTags;
using Homestead.WebApi.Features.Documents.v1.CreateComment;
using Homestead.WebApi.Features.Documents.v1.GetActivity;
using Homestead.WebApi.Features.Documents.v1.CreateDocument;
using Homestead.WebApi.Features.Documents.v1.CreateDocumentVersion;
using Homestead.WebApi.Features.Documents.v1.CreateParty;
using Homestead.WebApi.Features.Documents.v1.DeleteDocument;
using Homestead.WebApi.Features.Documents.v1.DeleteDocumentVersion;
using Homestead.WebApi.Features.Documents.v1.DeleteParty;
using Homestead.WebApi.Features.Documents.v1.DeleteTag;
using Homestead.WebApi.Features.Documents.v1.GetDocument;
using Homestead.WebApi.Features.Documents.v1.GetDocumentFile;
using Homestead.WebApi.Features.Documents.v1.GetDocumentVersion;
using Homestead.WebApi.Features.Documents.v1.GetDocumentVersions;
using Homestead.WebApi.Features.Documents.v1.GetDocuments;
using Homestead.WebApi.Features.Documents.v1.GetDocumentThumbnail;
using Homestead.WebApi.Features.Documents.v1.GetMyGroups;
using Homestead.WebApi.Features.Documents.v1.GetParties;
using Homestead.WebApi.Features.Documents.v1.GetParty;
using Homestead.WebApi.Features.Documents.v1.PatchDocument;
using Homestead.WebApi.Features.Documents.v1.PatchDocumentVersion;
using Homestead.WebApi.Features.Documents.v1.CreateTag;
using Homestead.WebApi.Features.Documents.v1.GetTags;
using Homestead.WebApi.Features.Documents.v1.UpdateParty;
using Homestead.WebApi.Features.Documents.v1.PatchTag;
using Homestead.WebApi.Features.Documents.v1.ReprocessDocumentVersion;
using Homestead.WebApi.Features.Documents.v1.RestoreDocuments;
using Homestead.WebApi.Features.Documents.v1.PermanentlyDeleteDocuments;
using Homestead.WebApi.Features.Documents.v1.EmptyTrash;

using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Features.Documents;

public static class DocumentsModule
{
    public static IVersionedEndpointRouteBuilder MapDocumentsEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/documents")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Documents")
            .RequireAuthorization(AuthPolicies.ActiveUser);

        BulkDeleteDocumentsEndpoint.Map(v1);
        BulkUpdateDocumentsEndpoint.Map(v1);
        CreateDocumentEndpoint.Map(v1);
        DeleteDocumentEndpoint.Map(v1);
        EmptyTrashEndpoint.Map(v1);
        GetDocumentEndpoint.Map(v1);
        GetDocumentFileEndpoint.Map(v1);
        GetDocumentThumbnailEndpoint.Map(v1);
        GetDocumentsEndpoint.Map(v1);
        PatchDocumentEndpoint.Map(v1);
        PermanentlyDeleteDocumentsEndpoint.Map(v1);
        RestoreDocumentsEndpoint.Map(v1);

        var versions = v1.MapGroup("/{documentId:guid}/versions");
        CreateDocumentVersionEndpoint.Map(versions);
        DeleteDocumentVersionEndpoint.Map(versions);
        GetDocumentVersionsEndpoint.Map(versions);
        GetDocumentVersionEndpoint.Map(versions);
        PatchDocumentVersionEndpoint.Map(versions);
        ReprocessDocumentVersionEndpoint.Map(versions);

        var comments = v1.MapGroup("/{documentId:guid}/comments");
        CreateCommentEndpoint.Map(comments);

        var activity = v1.MapGroup("/{documentId:guid}/activity");
        GetActivityEndpoint.Map(activity);

        var parties = v1.MapGroup("/parties");
        BulkDeletePartiesEndpoint.Map(parties);
        CreatePartyEndpoint.Map(parties);
        DeletePartyEndpoint.Map(parties);
        GetPartiesEndpoint.Map(parties);
        GetPartyEndpoint.Map(parties);
        UpdatePartyEndpoint.Map(parties);

        var tags = v1.MapGroup("/tags");
        BulkDeleteTagsEndpoint.Map(tags);
        CreateTagEndpoint.Map(tags);
        DeleteTagEndpoint.Map(tags);
        GetTagsEndpoint.Map(tags);
        PatchTagEndpoint.Map(tags);

        var groups = v1.MapGroup("/groups");
        GetMyGroupsEndpoint.Map(groups);

        return app;
    }
}
