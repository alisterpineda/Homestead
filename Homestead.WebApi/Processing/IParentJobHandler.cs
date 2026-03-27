namespace Homestead.WebApi.Processing;

/// <summary>
/// Marker interface for job handlers that spawn child jobs.
/// The processor keeps the parent job InProgress after HandleAsync returns,
/// and transitions it to a terminal status when all children complete.
/// </summary>
public interface IParentJobHandler : IJobHandler;
