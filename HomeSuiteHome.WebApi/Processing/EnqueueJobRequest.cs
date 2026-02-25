using HomeSuiteHome.WebApi.Domains.Processing;

namespace HomeSuiteHome.WebApi.Processing;

public class EnqueueJobRequest
{
    public ProcessingEntityType? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Payload { get; set; }
    public int Priority { get; set; }
    public Guid? CreatedByUserId { get; set; }
}
