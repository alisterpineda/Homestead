using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Processing;

/// <summary>
/// Catches the misconfigurations that break the queue silently rather than loudly: a heartbeat slower
/// than the lease it defends loses every long job's lease mid-run, and a non-positive interval turns a
/// timer into a spin or an exception on a background thread nobody is watching.
/// </summary>
public sealed class JobProcessingOptionsValidator : IValidateOptions<JobProcessingOptions>
{
    public ValidateOptionsResult Validate(string? name, JobProcessingOptions options)
    {
        var failures = new List<string>();

        if (options.LeaseDuration <= TimeSpan.Zero)
            failures.Add("Processing:LeaseDuration must be positive.");

        if (options.HeartbeatInterval <= TimeSpan.Zero)
            failures.Add("Processing:HeartbeatInterval must be positive.");

        if (options.PollInterval <= TimeSpan.Zero)
            failures.Add("Processing:PollInterval must be positive.");

        if (options.HeartbeatInterval >= options.LeaseDuration)
            failures.Add(
                "Processing:HeartbeatInterval must be shorter than Processing:LeaseDuration, or a running job loses its lease.");

        if (options.RetryBackoff?.Any(d => d < TimeSpan.Zero) == true)
            failures.Add("Processing:RetryBackoff entries must not be negative.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
