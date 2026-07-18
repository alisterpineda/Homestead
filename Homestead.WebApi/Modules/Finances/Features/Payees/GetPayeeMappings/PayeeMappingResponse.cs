namespace Homestead.WebApi.Modules.Finances.Features.Payees.GetPayeeMappings;

/// <summary>
/// A learned <c>raw-description → payee</c> mapping, surfaced on the payee edit dialog's
/// "learned from these descriptions" section. Shows the sample raw text a reviewer will recognise, how many
/// times it has been confirmed, and when it last changed.
/// </summary>
public record PayeeMappingResponse(
    Guid Id,
    string NormalizedKey,
    string SampleRawDescription,
    int TimesConfirmed,
    DateTimeOffset UpdatedAt);
