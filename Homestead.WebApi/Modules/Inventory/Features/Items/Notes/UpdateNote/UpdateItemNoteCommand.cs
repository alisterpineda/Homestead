using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.UpdateNote;

/// <summary>Full replace of a note's three user-editable fields (PUT semantics, not a patch).</summary>
public record UpdateItemNoteCommand : ICommand<ItemNoteResponse>
{
    internal Guid ItemId { get; init; }
    internal Guid NoteId { get; init; }

    /// <summary>Not nullable: the composer always sends the row's date back. Never in the future.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    public string? Subject { get; init; }

    /// <summary>Required unless <see cref="Subject"/> is set.</summary>
    public string? Body { get; init; }
}
