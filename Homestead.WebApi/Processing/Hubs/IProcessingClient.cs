using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Processing.Hubs;

public interface IProcessingClient
{
    Task JobStatusChanged(JobStatusChangedMessage message);
}

public record JobStatusChangedMessage(
    Guid JobId,
    string Status,
    string? EntityType,
    Guid? EntityId,
    string? DocumentName,
    string? Error,
    string? ErrorCode,
    Dictionary<string, string>? ErrorData);
