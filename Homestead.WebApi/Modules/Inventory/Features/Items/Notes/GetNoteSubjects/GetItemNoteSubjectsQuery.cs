using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.GetNoteSubjects;

/// <summary>
/// The distinct subjects already used on an item's notes, most recently used first — the composer's
/// autocomplete source. Null when the item is missing.
/// </summary>
public record GetItemNoteSubjectsQuery(Guid ItemId) : IRequest<IReadOnlyList<string>?>;
