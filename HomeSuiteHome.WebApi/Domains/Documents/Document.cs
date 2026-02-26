using HomeSuiteHome.WebApi.Domains.Identity;
using HomeSuiteHome.WebApi.Domains.Parties;
using HomeSuiteHome.WebApi.Domains.Tags;

namespace HomeSuiteHome.WebApi.Domains.Documents;

public class Document
{
    public const string FilesSubroot = "documents/files";
    public const string UploadsSubroot = "documents/uploads";

    public static string GetFilePath(string fileHash, string extension)
        => Path.Combine(FilesSubroot, fileHash[..4], $"{fileHash}{extension}");

    public string GetDownloadFileName()
        => $"{Name}{FileExtension}";

    public string GetPhysicalPath(string mediaPath)
        => Path.Combine(mediaPath, FilePath);

    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string ContentType { get; set; }
    public long FileSize { get; set; }
    public required string FilePath { get; set; }
    public required string FileHash { get; set; }
    public required string FileExtension { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DocumentDate { get; set; }
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public string? ExtractedText { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? OwnerUser { get; set; }

    public ICollection<DocumentTag> DocumentTags { get; set; } = [];
    public ICollection<DocumentParty> DocumentParties { get; set; } = [];
}