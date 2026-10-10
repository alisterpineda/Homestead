namespace Homestead.WebApi.Modules.Admin.Features.Backup.ConnectBackupTarget;

public record ConnectBackupTargetCommand(string TargetKey);

/// <param name="UserCode">The short code the admin types at <paramref name="VerificationUrl"/>.</param>
/// <param name="PollToken">
/// Sealed state to hand back to the poll endpoint. The device code is inside it, encrypted — it is a
/// bearer secret for the length of the flow and never reaches the browser in the clear.
/// </param>
public record ConnectBackupTargetResponse(
    string UserCode,
    string VerificationUrl,
    DateTimeOffset ExpiresAt,
    int IntervalSeconds,
    string PollToken);
