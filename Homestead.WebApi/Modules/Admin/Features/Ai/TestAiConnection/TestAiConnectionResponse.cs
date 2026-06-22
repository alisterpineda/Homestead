namespace Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;

public record TestAiConnectionResponse(bool Success, string? Message, int? LatencyMs);
