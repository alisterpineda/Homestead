using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Domain.Parties;
using NpgsqlTypes;

namespace Homestead.WebApi.Domain.Documents;

public class Document
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public NpgsqlTsVector? SearchVector { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public AppUser? DeletedByUser { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? OwnerUser { get; set; }

    public Guid? GroupId { get; set; }
    public Group? Group { get; set; }

    public ICollection<DocumentVersion> Versions { get; set; } = [];
    public ICollection<DocumentComment> Comments { get; set; } = [];
    public ICollection<DocumentTag> DocumentTags { get; set; } = [];
    public ICollection<DocumentParty> DocumentParties { get; set; } = [];
    public ICollection<DocumentSuggestion> Suggestions { get; set; } = [];

    /// <summary>
    /// Applies a summary to an existing document and stamps modified-metadata. Shared by the single-doc
    /// summarize path and the classifier's summary application so the two cannot diverge. Not used at
    /// creation, where the summary is part of the initial state (no modified stamp).
    /// </summary>
    public void ApplySummary(string summary, Guid? updatedByUserId)
    {
        Summary = summary;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedByUserId = updatedByUserId;
    }
}
