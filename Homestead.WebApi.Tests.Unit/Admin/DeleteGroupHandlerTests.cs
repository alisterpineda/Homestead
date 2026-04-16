using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Modules.Admin.Features.Groups.DeleteGroup;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

// DeleteGroupHandler uses ExecuteUpdateAsync + IgnoreQueryFilters which aren't
// supported by the EF Core InMemory provider. Audit logging for group deletion
// is validated through integration tests instead.
public class DeleteGroupHandlerTests
{
    [Fact]
    public void Handler_AcceptsAuditContextDependency()
    {
        // Verify the handler can be constructed with IAuditContext
        using var db = TestAppDbContext.Create();
        var audit = Substitute.For<IAuditContext>();
        var handler = new DeleteGroupHandler(db, audit);

        Assert.NotNull(handler);
    }
}
