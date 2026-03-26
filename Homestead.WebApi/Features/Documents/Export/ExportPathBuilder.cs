using System.Text.RegularExpressions;

namespace Homestead.WebApi.Features.Documents.Export;

public record ExportDocumentMetadata(
    string DocumentName,
    string FileExtension,
    List<string> TagPaths,
    List<string> PartyNames);

public interface IExportPathBuilder
{
    List<string> BuildPaths(ExportDocumentMetadata document);
}

public partial class TagBasedExportPathBuilder : IExportPathBuilder
{
    private const int MaxFileNameLength = 200;
    private const string UntaggedFolder = "_Untagged";
    private const string FallbackDocumentName = "Untitled";
    private const string FallbackTagSegment = "Unknown Tag";

    public List<string> BuildPaths(ExportDocumentMetadata document)
    {
        var fileName = BuildFileName(document.DocumentName, document.PartyNames, document.FileExtension);

        if (document.TagPaths.Count == 0)
            return [Path.Combine(UntaggedFolder, fileName)];

        return document.TagPaths
            .Select(tagPath => Path.Combine(SanitizeTagPath(tagPath), fileName))
            .ToList();
    }

    internal static string BuildFileName(string documentName, List<string> partyNames, string fileExtension)
    {
        var sanitizedName = SanitizeSegment(documentName);
        if (string.IsNullOrEmpty(sanitizedName))
            sanitizedName = FallbackDocumentName;

        if (partyNames.Count > 0)
        {
            var parties = string.Join(", ", partyNames.Select(p => SanitizeSegment(p)).Where(p => p.Length > 0));
            if (parties.Length > 0)
                sanitizedName = $"{sanitizedName} - {parties}";
        }

        if (sanitizedName.Length > MaxFileNameLength)
            sanitizedName = sanitizedName[..MaxFileNameLength].TrimEnd();

        return $"{sanitizedName}{fileExtension}";
    }

    internal static string SanitizeTagPath(string tagPath)
    {
        var segments = tagPath.Split('/');
        var sanitized = segments
            .Select(SanitizeSegment)
            .Select(s => string.IsNullOrEmpty(s) ? FallbackTagSegment : s);
        return Path.Combine(sanitized.ToArray());
    }

    internal static string SanitizeSegment(string segment)
    {
        // Replace illegal OS characters and control chars with underscore
        var result = IllegalCharsRegex().Replace(segment, "_");
        // Collapse consecutive whitespace
        result = ConsecutiveWhitespaceRegex().Replace(result, " ");
        // Trim leading/trailing dots and spaces (Windows restriction)
        result = result.Trim('.', ' ');
        return result;
    }

    [GeneratedRegex(@"[\\/:*?""<>|\x00-\x1F]")]
    private static partial Regex IllegalCharsRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ConsecutiveWhitespaceRegex();
}
