namespace HomeSuiteHome.WebApi.Domains.Documents;

public class Document
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long FileSize { get; set; }
    public required string FilePath { get; set; }
    public required string FileHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}