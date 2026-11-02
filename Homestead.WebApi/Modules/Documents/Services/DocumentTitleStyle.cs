namespace Homestead.WebApi.Modules.Documents.Services;

// The product-defined house style for document titles (docs/product-design/documents/overview.md, "Suggested title
// style"). Shared by the AI classifier's prompt (AiClassificationService) and the MCP server instructions, so a title
// reads the same whether the classifier suggests it or the user's own Claude client renames a document.
public static class DocumentTitleStyle
{
    public const string Form = "\"Issuer Type - Detail - Period\", in Title Case, ideally 60 characters or fewer";

    // The examples deliberately name no eval fixture's issuer or subject (Homestead.WebApi.Tests.Eval/Fixtures), so
    // the eval can't score the prompt's own answers.
    public const string Examples =
        "\"Midas Invoice - Honda Civic Brake Repair\", \"Hydro One Bill - March 2025\", " +
        "\"Acme Pay Stub - Jane Doe - March 2025\", \"TD Joint Chequing Statement - March 2025\", " +
        "\"Amazon Order - #702-1234567\"";

    // One rule per entry, without a list marker.
    public static readonly IReadOnlyList<string> Rules =
    [
        "Separate title parts with \" - \" (a plain hyphen between spaces), and add no parts beyond issuer and type, detail and period",
        "If the title runs long, shorten the detail first; never drop the issuer, type or period",
        "Never put account, card, policy, SIN or licence numbers in the title, not even their last digits",
        "Write the title in English, keeping names as printed (for example Hydro-Québec)",
    ];
}
