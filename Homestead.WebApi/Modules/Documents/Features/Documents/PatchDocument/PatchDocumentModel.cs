namespace Homestead.WebApi.Modules.Documents.Features.Documents.PatchDocument;

public class PatchDocumentModel
{
    public string Name { get; set; } = string.Empty;
    public DateOnly? DocumentDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string Notes { get; set; } = string.Empty;
    public List<string> TagPaths { get; set; } = [];
    public List<Guid> PartyIds { get; set; } = [];
    public Guid? GroupId { get; set; }
}
