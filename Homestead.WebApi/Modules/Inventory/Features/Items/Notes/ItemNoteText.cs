namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes;

/// <summary>Text normalisation shared by the note commands.</summary>
public static class ItemNoteText
{
    /// <summary>Trims the body; the validators have already rejected a blank one.</summary>
    public static string NormalizeBody(string? body) => body?.Trim() ?? "";

    /// <summary>The one-line label an audit row keeps for a note: the first line of the body.</summary>
    public static string AuditDescription(string body)
    {
        var newline = body.IndexOfAny(['\r', '\n']);
        return (newline >= 0 ? body[..newline] : body).Trim();
    }
}
