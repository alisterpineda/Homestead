namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.CreateNote;

public record CreateItemNoteCommand
{
    /// <summary>The owning item, taken from the route by the endpoint (never from the body).</summary>
    internal Guid ItemId { get; init; }

    /// <summary>When it happened; null means "now" on the server clock. Never in the future.</summary>
    public DateTimeOffset? OccurredAt { get; init; }

    /// <summary>The note text; required, trimmed, at most 4000 characters.</summary>
    public string? Body { get; init; }
}
