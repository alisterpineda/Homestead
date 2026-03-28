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
    public NpgsqlTsVector? SearchVector { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
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
}
