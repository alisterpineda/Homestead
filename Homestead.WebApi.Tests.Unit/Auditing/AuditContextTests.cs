using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Parties;
using Homestead.WebApi.Features.Auditing;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class AuditContextTests
{
    private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();

    [Fact]
    public void HasEntries_Initially_ReturnsFalse()
    {
        var context = new AuditContext();
        Assert.False(context.HasEntries);
    }

    [Fact]
    public void Created_SetsHasEntriesToTrue()
    {
        var context = new AuditContext();
        context.Created("Party", Guid.NewGuid());
        Assert.True(context.HasEntries);
    }

    [Fact]
    public void FlushTo_Created_CallsLogCreated()
    {
        var context = new AuditContext();
        var id = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        context.Created("Party", id, parentId);
        context.FlushTo(_auditLogger);

        _auditLogger.Received(1).LogCreated("Party", id, parentId);
    }

    [Fact]
    public void FlushTo_Track_ComputesDiffFromBeforeAndAfterSnapshots()
    {
        var context = new AuditContext();
        var party = new Party
        {
            Id = Guid.NewGuid(),
            Name = "Original",
            Description = "Desc"
        };

        context.Track<PartySnapshot, Party>(party, "Party", party.Id);

        // Mutate the entity after tracking
        party.Name = "Updated";

        context.FlushTo(_auditLogger);

        _auditLogger.Received(1).LogUpdated(
            "Party",
            party.Id,
            Arg.Is<List<FieldChange>>(c =>
                c.Count == 1 &&
                c[0] is ScalarFieldChange &&
                ((ScalarFieldChange)c[0]).Field == "Name" &&
                ((ScalarFieldChange)c[0]).OldValue == "Original" &&
                ((ScalarFieldChange)c[0]).NewValue == "Updated"),
            null);
    }

    [Fact]
    public void FlushTo_TrackWithNoChanges_CallsLogUpdatedWithEmptyList()
    {
        var context = new AuditContext();
        var party = new Party
        {
            Id = Guid.NewGuid(),
            Name = "Same",
            Description = "Same"
        };

        context.Track<PartySnapshot, Party>(party, "Party", party.Id);

        // No mutation
        context.FlushTo(_auditLogger);

        _auditLogger.Received(1).LogUpdated(
            "Party",
            party.Id,
            Arg.Is<List<FieldChange>>(c => c.Count == 0),
            null);
    }

    [Fact]
    public void FlushTo_TrackWithDeferredDiff_ComputesDiff()
    {
        var context = new AuditContext();
        var id = Guid.NewGuid();
        var changes = new List<FieldChange> { new ScalarFieldChange("Name", "Old", "New") };

        context.Track(() => changes, "Party", id);
        context.FlushTo(_auditLogger);

        _auditLogger.Received(1).LogUpdated("Party", id, changes, null);
    }

    [Fact]
    public void FlushTo_Deleted_CallsLogDeleted()
    {
        var context = new AuditContext();
        var id = Guid.NewGuid();

        context.Deleted("Tag", id, description: "My Tag");
        context.FlushTo(_auditLogger);

        _auditLogger.Received(1).LogDeleted("Tag", id, "My Tag", null);
    }

    [Fact]
    public void FlushTo_SoftDeleted_CallsLogSoftDeleted()
    {
        var context = new AuditContext();
        var id = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        context.SoftDeleted("Document", id, description: "doc.pdf", parentId);
        context.FlushTo(_auditLogger);

        _auditLogger.Received(1).LogSoftDeleted("Document", id, "doc.pdf", parentId);
    }

    [Fact]
    public void FlushTo_Restored_CallsLogRestored()
    {
        var context = new AuditContext();
        var id = Guid.NewGuid();

        context.Restored("Document", id, description: "doc.pdf");
        context.FlushTo(_auditLogger);

        _auditLogger.Received(1).LogRestored("Document", id, "doc.pdf", null);
    }

    [Fact]
    public void FlushTo_ClearsEntries()
    {
        var context = new AuditContext();
        context.Created("Party", Guid.NewGuid());

        context.FlushTo(_auditLogger);

        Assert.False(context.HasEntries);

        // Second flush is a no-op
        context.FlushTo(_auditLogger);
        _auditLogger.Received(1).LogCreated(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>());
    }

    [Fact]
    public void FlushTo_MultipleEntries_AllFlushedInOrder()
    {
        var context = new AuditContext();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        context.Created("Party", id1);
        context.Deleted("Tag", id2, description: "tag");
        context.Restored("Document", id3);

        context.FlushTo(_auditLogger);

        Received.InOrder(() =>
        {
            _auditLogger.LogCreated("Party", id1, null);
            _auditLogger.LogDeleted("Tag", id2, "tag", null);
            _auditLogger.LogRestored("Document", id3, null, null);
        });
    }
}
