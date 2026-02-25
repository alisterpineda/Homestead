using HomeSuiteHome.WebApi.Domains.Processing;

namespace HomeSuiteHome.WebApi.Processing.Hubs;

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
    string? Error);
