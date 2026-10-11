namespace Homestead.WebApi.Modules.Finances.Features.Payees.MergePayee;

/// <summary>Request body for merge: the payee to keep.</summary>
public record MergePayeeRequest(Guid TargetId);
