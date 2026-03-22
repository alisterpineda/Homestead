using Homestead.WebApi.Domains.Groups;
using Homestead.WebApi.Features.Admin.v1.DeleteGroup;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

// DeleteGroupHandler uses ExecuteUpdateAsync + IgnoreQueryFilters which aren't
// supported by the EF Core InMemory provider. Audit logging for group deletion
// is validated through integration tests instead.
public class DeleteGroupHandlerTests
{
    [Fact]
    public void Handler_AcceptsAuditLoggerDependency()
    {
        // Verify the handler can be constructed with IAuditLogger
        using var db = TestAppDbContext.Create();
        var auditLogger = Substitute.For<IAuditLogger>();
        var handler = new DeleteGroupHandler(db, auditLogger);

        Assert.NotNull(handler);
    }
}
