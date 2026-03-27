namespace Homestead.WebApi.Processing;

public class JobProcessingOptions
{
    public const string SectionName = "Processing";

    public int MaxConcurrency { get; set; } = 1;
}
