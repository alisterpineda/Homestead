namespace Homestead.WebApi.Domains.Documents;

public class DocumentVersion
{
    public const string FilesSubroot = "documents/files";
    public const string UploadsSubroot = "documents/uploads";

    public static string GetFilePath(string fileHash, string extension)
        => Path.Combine(FilesSubroot, fileHash[..4], $"{fileHash}{extension}");

    public string GetDownloadFileName(string documentName)
        => $"{documentName}{FileExtension}";

    public string GetPhysicalPath(string mediaPath)
        => Path.Combine(mediaPath, FilePath);

    public Guid Id { get; set; }
    public int VersionNumber { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }

    public required string ContentType { get; set; }
    public long FileSize { get; set; }
    public required string FilePath { get; set; }
    public required string FileHash { get; set; }
    public required string FileExtension { get; set; }
    public string? ExtractedText { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
}
