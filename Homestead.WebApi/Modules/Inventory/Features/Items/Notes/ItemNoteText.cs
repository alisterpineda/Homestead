namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes;

/// <summary>Text normalisation shared by the note commands.</summary>
public static class ItemNoteText
{
    /// <summary>Trims; a blank string becomes null (the stored shape of an absent subject).</summary>
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Trims the body; an absent body is stored as "" (allowed only alongside a subject).</summary>
    public static string NormalizeBody(string? body) => body?.Trim() ?? "";

    /// <summary>
    /// The one-line label an audit row keeps for a note: the subject when set, otherwise the first
    /// line of the body.
    /// </summary>
    public static string AuditDescription(string? subject, string body)
    {
        if (!string.IsNullOrWhiteSpace(subject))
            return subject.Trim();

        var newline = body.IndexOfAny(['\r', '\n']);
        return (newline >= 0 ? body[..newline] : body).Trim();
    }
}
