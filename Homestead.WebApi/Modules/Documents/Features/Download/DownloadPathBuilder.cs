using Homestead.WebApi.Common.Files;

namespace Homestead.WebApi.Modules.Documents.Features.Download;

public record DownloadDocumentMetadata(
    string DocumentName,
    string FileExtension,
    List<string> TagPaths,
    List<string> PartyNames);

public interface IDownloadPathBuilder
{
    List<string> BuildPaths(DownloadDocumentMetadata document);
}

public class TagBasedDownloadPathBuilder : IDownloadPathBuilder
{
    private const int MaxFileNameLength = SafeFileName.MaxFileNameLength;
    private const string UntaggedFolder = "_Untagged";
    private const string FallbackDocumentName = "Untitled";
    private const string FallbackTagSegment = "Unknown Tag";

    public List<string> BuildPaths(DownloadDocumentMetadata document)
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

    // Delegates to the shared sanitizer (Common/Files/SafeFileName.cs), which the backup mirror also
    // uses so the two file trees can never diverge on what characters are legal.
    internal static string SanitizeSegment(string segment) => SafeFileName.SanitizeSegment(segment);
}
