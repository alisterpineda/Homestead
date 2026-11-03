using Homestead.WebApi.Processing;

namespace Homestead.WebApi.Tests.Unit.Processing;

// The message lands in the job's Error column. A handler-declared budget need not be whole minutes, and
// "0.005 minutes" says nothing useful.
public class JobTimeoutExceptionTests
{
    [Fact]
    public void Message_WholeMinutes_ReadsInMinutes()
    {
        var ex = new JobTimeoutException(Guid.Empty, TimeSpan.FromMinutes(5));

        Assert.EndsWith("timed out after 5 minutes", ex.Message);
        Assert.Equal(TimeSpan.FromMinutes(5), ex.Timeout);
    }

    [Fact]
    public void Message_FractionalBudget_ReadsAsATimeSpan()
    {
        var ex = new JobTimeoutException(Guid.Empty, TimeSpan.FromMilliseconds(300));

        Assert.EndsWith("timed out after 00:00:00.3000000", ex.Message);
    }
}
