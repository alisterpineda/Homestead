namespace Homestead.WebApi.Processing;

public interface IJobProcessorControl
{
    /// <summary>
    /// Pauses the job processor. Waits for any in-flight job to finish, then
    /// prevents new jobs from starting until the returned handle is disposed.
    /// </summary>
    Task<IAsyncDisposable> PauseAsync(CancellationToken ct = default);
}
