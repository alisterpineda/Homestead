using System.Text.Json.Serialization;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks;

/// <summary>
/// Whether the linked document can be opened by the caller. <see cref="Trashed"/> documents render
/// greyed with no deep link (restore revives them); <see cref="Unavailable"/> means the document
/// exists but is not accessible to the caller — deliberately indistinguishable from "gone" so link
/// rows never leak the existence of documents outside the caller's access (a *purged* document can
/// never appear here at all: its links cascade-delete with it).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentLinkAvailability
{
    Available,
    Trashed,
    Unavailable,
}

public record TransactionDocumentLinkResponse
{
    public required Guid DocumentId { get; init; }

    /// <summary>Null when <see cref="Availability"/> is <see cref="DocumentLinkAvailability.Unavailable"/>.</summary>
    public string? DocumentName { get; init; }

    public required TransactionDocumentLinkOrigin Origin { get; init; }
    public required DocumentLinkAvailability Availability { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
