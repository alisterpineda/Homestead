using Asp.Versioning.Builder;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Documents.Features.Documents.BulkDeleteDocuments;
using Homestead.WebApi.Modules.Documents.Features.Documents.BulkReprocessDocuments;
using Homestead.WebApi.Modules.Documents.Features.Parties.BulkDeleteParties;
using Homestead.WebApi.Modules.Documents.Features.Documents.BulkUpdateDocuments;
using Homestead.WebApi.Modules.Documents.Features.Tags.BulkDeleteTags;
using Homestead.WebApi.Modules.Documents.Features.Comments.CreateComment;
using Homestead.WebApi.Modules.Documents.Features.Activity.GetActivity;
using Homestead.WebApi.Modules.Documents.Features.Documents.CreateDocument;
using Homestead.WebApi.Modules.Documents.Features.Versions.CreateDocumentVersion;
using Homestead.WebApi.Modules.Documents.Features.Parties.CreateParty;
using Homestead.WebApi.Modules.Documents.Features.Documents.DeleteDocument;
using Homestead.WebApi.Modules.Documents.Features.Versions.DeleteDocumentVersion;
using Homestead.WebApi.Modules.Documents.Features.Parties.DeleteParty;
using Homestead.WebApi.Modules.Documents.Features.Tags.DeleteTag;
using Homestead.WebApi.Modules.Documents.Features.Documents.GetDocument;
using Homestead.WebApi.Modules.Documents.Features.Documents.GetDocumentFile;
using Homestead.WebApi.Modules.Documents.Features.Versions.GetDocumentVersion;
using Homestead.WebApi.Modules.Documents.Features.Versions.GetDocumentVersions;
using Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;
using Homestead.WebApi.Modules.Documents.Features.Documents.GetDocumentThumbnail;
using Homestead.WebApi.Modules.Documents.Features.MyGroups.GetMyGroups;
using Homestead.WebApi.Modules.Documents.Features.Parties.GetParties;
using Homestead.WebApi.Modules.Documents.Features.Parties.GetPartiesByIds;
using Homestead.WebApi.Modules.Documents.Features.Parties.GetParty;
using Homestead.WebApi.Modules.Documents.Features.Documents.PatchDocument;
using Homestead.WebApi.Modules.Documents.Features.Versions.PatchDocumentVersion;
using Homestead.WebApi.Modules.Documents.Features.Tags.CreateTag;
using Homestead.WebApi.Modules.Documents.Features.Tags.GetTags;
using Homestead.WebApi.Modules.Documents.Features.Tags.GetTagsByIds;
using Homestead.WebApi.Modules.Documents.Features.Parties.UpdateParty;
using Homestead.WebApi.Modules.Documents.Features.Tags.PatchTag;
using Homestead.WebApi.Modules.Documents.Features.Versions.ReprocessDocumentVersion;
using Homestead.WebApi.Modules.Documents.Features.Documents.RestoreDocuments;
using Homestead.WebApi.Modules.Documents.Features.Documents.PermanentlyDeleteDocuments;
using Homestead.WebApi.Modules.Documents.Features.Documents.EmptyTrash;
using Homestead.WebApi.Modules.Documents.Features.Documents.BulkClassifyDocuments;
using Homestead.WebApi.Modules.Documents.Features.Documents.BulkSummarizeDocuments;
using Homestead.WebApi.Modules.Documents.Features.Documents.SummarizeDocument;
using Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveSuggestion;
using Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineSuggestion;
using Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveAllSuggestions;
using Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineAllSuggestions;
using Homestead.WebApi.Modules.Documents.Features.Download;

using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Documents.Features.Settings.GetDocumentSettings;
using Homestead.WebApi.Modules.Documents.Features.Settings.PatchDocumentSettings;
using Homestead.WebApi.Modules.Documents.Features.TextExtractionEngines.GetTextExtractionEngines;

namespace Homestead.WebApi.Modules.Documents;

public static class DocumentsModule
{
    public static IVersionedEndpointRouteBuilder MapDocumentsEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/documents")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Documents")
            .RequireAuthorization(AuthPolicies.ActiveUser)
            .WithCommandTransaction();

        BulkDeleteDocumentsEndpoint.Map(v1);
        BulkClassifyDocumentsEndpoint.Map(v1);
        BulkSummarizeDocumentsEndpoint.Map(v1);
        BulkReprocessDocumentsEndpoint.Map(v1);
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

        SummarizeDocumentEndpoint.Map(v1);
        GetTextExtractionEnginesEndpoint.Map(v1);

        var versions = v1.MapGroup("/{documentId:guid}/versions");
        CreateDocumentVersionEndpoint.Map(versions);
        DeleteDocumentVersionEndpoint.Map(versions);
        GetDocumentVersionsEndpoint.Map(versions);
        GetDocumentVersionEndpoint.Map(versions);
        PatchDocumentVersionEndpoint.Map(versions);
        ReprocessDocumentVersionEndpoint.Map(versions);

        var suggestions = v1.MapGroup("/{documentId:guid}/suggestions");
        ApproveSuggestionEndpoint.Map(suggestions);
        DeclineSuggestionEndpoint.Map(suggestions);
        ApproveAllSuggestionsEndpoint.Map(suggestions);
        DeclineAllSuggestionsEndpoint.Map(suggestions);

        var comments = v1.MapGroup("/{documentId:guid}/comments");
        CreateCommentEndpoint.Map(comments);

        var activity = v1.MapGroup("/{documentId:guid}/activity");
        GetActivityEndpoint.Map(activity);

        var parties = v1.MapGroup("/parties");
        BulkDeletePartiesEndpoint.Map(parties);
        CreatePartyEndpoint.Map(parties);
        DeletePartyEndpoint.Map(parties);
        GetPartiesEndpoint.Map(parties);
        GetPartiesByIdsEndpoint.Map(parties);
        GetPartyEndpoint.Map(parties);
        UpdatePartyEndpoint.Map(parties);

        var tags = v1.MapGroup("/tags");
        BulkDeleteTagsEndpoint.Map(tags);
        CreateTagEndpoint.Map(tags);
        DeleteTagEndpoint.Map(tags);
        GetTagsEndpoint.Map(tags);
        GetTagsByIdsEndpoint.Map(tags);
        PatchTagEndpoint.Map(tags);

        var groups = v1.MapGroup("/groups");
        GetMyGroupsEndpoint.Map(groups);

        var downloads = v1.MapGroup("/download");
        DownloadDocumentsEndpoint.Map(downloads);
        GetDownloadFileEndpoint.Map(downloads);

        var settings = v1.MapGroup("/settings")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        GetDocumentSettingsEndpoint.Map(settings);
        PatchDocumentSettingsEndpoint.Map(settings);

        return app;
    }
}
