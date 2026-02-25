using HomeSuiteHome.WebApi.Domains.Processing;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Processing.Hubs;

public class ProcessingNotifier(
    IHubContext<ProcessingHub, IProcessingClient> hubContext,
    AppDbContext db) : IProcessingNotifier
{
    public async Task NotifyJobStatusAsync(
        Guid jobId,
        ProcessingJobStatus status,
        ProcessingEntityType? entityType,
        Guid? entityId,
        string? documentName,
        string? error,
        Guid? userId,
        CancellationToken ct)
    {
        if (!userId.HasValue)
            return;

        // SignalR identifies users by ClaimTypes.NameIdentifier (the OIDC subject),
        // not by the internal AppUser.Id. Look up the external subject.
        var externalSubject = await db.AppUsers
            .Where(u => u.Id == userId.Value)
            .Select(u => u.ExternalSubject)
            .FirstOrDefaultAsync(ct);

        if (externalSubject is null)
            return;

        var message = new JobStatusChangedMessage(
            jobId,
            status.ToString(),
            entityType?.ToString(),
            entityId,
            documentName,
            error);

        await hubContext.Clients.User(externalSubject).JobStatusChanged(message);
    }
}
