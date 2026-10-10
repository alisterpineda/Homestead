namespace Homestead.WebApi.Modules.Documents.Features.Versions.ReprocessDocumentVersion;

public class ReprocessDocumentVersionCommand
{
    public Guid DocumentId { get; set; }
    public Guid VersionId { get; set; }

    // Null means "configured default". A non-null key is validated at enqueue and rejected with a 400 if
    // it is unknown or unavailable — see ITextExtractionEngineResolver.
    public string? EngineKey { get; set; }
}
