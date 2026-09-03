using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.CreateNote;

public record CreateItemNoteCommand : ICommand<ItemNoteResponse>
{
    /// <summary>The owning item, taken from the route by the endpoint (never from the body).</summary>
    internal Guid ItemId { get; init; }

    /// <summary>When it happened; null means "now" on the server clock. Never in the future.</summary>
    public DateTimeOffset? OccurredAt { get; init; }

    public string? Subject { get; init; }

    /// <summary>Required unless <see cref="Subject"/> is set.</summary>
    public string? Body { get; init; }
}
