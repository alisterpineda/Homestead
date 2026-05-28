using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Modules.Documents.Services;

public static class DocumentSuggestionReverter
{
    /// <summary>
    /// Undoes the document-side effect of an AI suggestion: removes the AI-applied tag/party
    /// link or restores the original name. Requires the document's DocumentTags and
    /// DocumentParties collections to be loaded. Returns true if the search vector needs updating.
    /// </summary>
    public static bool Revert(Document document, DocumentSuggestion suggestion)
    {
        switch (suggestion.Field)
        {
            case DocumentSuggestionField.Tags:
                var tagId = Guid.Parse(suggestion.SuggestedValue);
                var docTag = document.DocumentTags.FirstOrDefault(dt => dt.TagId == tagId);
                if (docTag is not null)
                    document.DocumentTags.Remove(docTag);
                return false;

            case DocumentSuggestionField.Parties:
                var partyId = Guid.Parse(suggestion.SuggestedValue);
                var docParty = document.DocumentParties.FirstOrDefault(dp => dp.PartyId == partyId);
                if (docParty is not null)
                    document.DocumentParties.Remove(docParty);
                return false;

            case DocumentSuggestionField.Name:
                if (suggestion.OriginalValue is not null)
                {
                    document.Name = suggestion.OriginalValue;
                    return true;
                }
                return false;

            default:
                return false;
        }
    }
}
