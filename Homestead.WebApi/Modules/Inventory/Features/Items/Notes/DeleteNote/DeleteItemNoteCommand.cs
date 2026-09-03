using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.DeleteNote;

public record DeleteItemNoteCommand(Guid ItemId, Guid NoteId) : ICommand<bool>;
