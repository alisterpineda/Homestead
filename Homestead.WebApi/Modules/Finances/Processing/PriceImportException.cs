using Homestead.WebApi.Processing;

namespace Homestead.WebApi.Modules.Finances.Processing;

public class PriceImportException : Exception, IStructuredJobError
{
    public PriceImportException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
        ErrorData = new Dictionary<string, string>();
    }

    public string ErrorCode { get; }
    public Dictionary<string, string> ErrorData { get; }
}
