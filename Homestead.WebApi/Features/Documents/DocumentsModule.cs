using Asp.Versioning.Builder;
using Homestead.WebApi.Features.Documents.Documents.BulkDeleteDocuments;
using Homestead.WebApi.Features.Documents.Parties.BulkDeleteParties;
using Homestead.WebApi.Features.Documents.Documents.BulkUpdateDocuments;
using Homestead.WebApi.Features.Documents.Tags.BulkDeleteTags;
using Homestead.WebApi.Features.Documents.Comments.CreateComment;
using Homestead.WebApi.Features.Documents.Activity.GetActivity;
using Homestead.WebApi.Features.Documents.Documents.CreateDocument;
using Homestead.WebApi.Features.Documents.Documents.CreateDocumentVersion;
using Homestead.WebApi.Features.Documents.Parties.CreateParty;
using Homestead.WebApi.Features.Documents.Documents.DeleteDocument;
using Homestead.WebApi.Features.Documents.Documents.DeleteDocumentVersion;
using Homestead.WebApi.Features.Documents.Parties.DeleteParty;
using Homestead.WebApi.Features.Documents.Tags.DeleteTag;
using Homestead.WebApi.Features.Documents.Documents.GetDocument;
using Homestead.WebApi.Features.Documents.Documents.GetDocumentFile;
using Homestead.WebApi.Features.Documents.Documents.GetDocumentVersion;
using Homestead.WebApi.Features.Documents.Documents.GetDocumentVersions;
using Homestead.WebApi.Features.Documents.Documents.GetDocuments;
using Homestead.WebApi.Features.Documents.Documents.GetDocumentThumbnail;
using Homestead.WebApi.Features.Documents.MyGroups.GetMyGroups;
using Homestead.WebApi.Features.Documents.Parties.GetParties;
using Homestead.WebApi.Features.Documents.Parties.GetParty;
using Homestead.WebApi.Features.Documents.Documents.PatchDocument;
using Homestead.WebApi.Features.Documents.Documents.PatchDocumentVersion;
using Homestead.WebApi.Features.Documents.Tags.CreateTag;
using Homestead.WebApi.Features.Documents.Tags.GetTags;
using Homestead.WebApi.Features.Documents.Parties.UpdateParty;
using Homestead.WebApi.Features.Documents.Tags.PatchTag;
using Homestead.WebApi.Features.Documents.Versions.ReprocessDocumentVersion;
using Homestead.WebApi.Features.Documents.Documents.RestoreDocuments;
using Homestead.WebApi.Features.Documents.Documents.PermanentlyDeleteDocuments;
using Homestead.WebApi.Features.Documents.Documents.EmptyTrash;
using Homestead.WebApi.Features.Documents.TextExtractionEngineSummaries.GetTextExtractionEngineSummaries;

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

        GetTextExtractionEngineSummariesEndpoint.Map(v1);

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
