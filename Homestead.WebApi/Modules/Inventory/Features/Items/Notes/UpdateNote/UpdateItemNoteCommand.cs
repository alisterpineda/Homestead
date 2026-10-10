namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.UpdateNote;

/// <summary>Full replace of a note's two user-editable fields (PUT semantics, not a patch).</summary>
public record UpdateItemNoteCommand
{
    internal Guid ItemId { get; init; }
    internal Guid NoteId { get; init; }

    /// <summary>Not nullable: the composer always sends the row's date back. Never in the future.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The note text; required, trimmed, at most 4000 characters.</summary>
    public string? Body { get; init; }
}
