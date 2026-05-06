using System.Net;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

internal static class MatchSnippetSanitizer
{
    public const string StartSentinel = "";
    public const string StopSentinel = "";

    public const string HeadlineOptions =
        $"StartSel={StartSentinel}, StopSel={StopSentinel}, MaxFragments=1, MaxWords=22, MinWords=10, ShortWord=3, HighlightAll=FALSE";

    public static string? Sanitize(string? snippet)
    {
        if (string.IsNullOrEmpty(snippet) || !snippet.Contains(StartSentinel))
        {
            return null;
        }

        return WebUtility.HtmlEncode(snippet)
            .Replace(StartSentinel, "<mark>")
            .Replace(StopSentinel, "</mark>");
    }
}
