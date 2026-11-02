using System.Net;

namespace Homestead.WebApi.Modules.Mcp;

// GetDocumentsHandler's MatchSnippet is HTML for the SPA (MatchSnippetSanitizer: the text encoded, the highlights as
// <mark>). A model wants plain text: drop the highlight tags, then decode — in that order, so a document that
// literally said "<mark>" (encoded as &lt;mark&gt;) keeps it.
public static class SnippetDecoder
{
    public static string? Decode(string? snippet)
    {
        if (string.IsNullOrEmpty(snippet))
            return null;

        return WebUtility.HtmlDecode(snippet.Replace("<mark>", "").Replace("</mark>", ""));
    }
}
