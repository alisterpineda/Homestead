using Homestead.WebApi.Domains.Processing;
using Microsoft.AspNetCore.SignalR;

namespace Homestead.WebApi.Processing.Hubs;

public class ProcessingNotifier(
    IHubContext<ProcessingHub, IProcessingClient> hubContext) : IProcessingNotifier
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

        var message = new JobStatusChangedMessage(
            jobId,
            status.ToString(),
            entityType?.ToString(),
            entityId,
            documentName,
            error);

        await hubContext.Clients.User(userId.Value.ToString()).JobStatusChanged(message);
    }
}
