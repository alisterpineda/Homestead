namespace Homestead.WebApi.Processing;

public class JobSchedulingOptions
{
    public const string SectionName = "Processing:Scheduling";

    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
}
