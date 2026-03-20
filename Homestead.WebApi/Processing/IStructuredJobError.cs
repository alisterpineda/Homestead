namespace Homestead.WebApi.Processing;

public interface IStructuredJobError
{
    string ErrorCode { get; }
    Dictionary<string, string> ErrorData { get; }
}
