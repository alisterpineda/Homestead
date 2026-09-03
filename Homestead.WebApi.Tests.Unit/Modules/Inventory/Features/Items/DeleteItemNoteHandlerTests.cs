using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Modules.Inventory.Features.Items.Notes.DeleteNote;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

public class DeleteItemNoteHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly DeleteItemNoteHandler _sut;

    public DeleteItemNoteHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new DeleteItemNoteHandler(_db, _audit, unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_MissingNote_ReturnsFalse()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        Assert.False(await _sut.Handle(new DeleteItemNoteCommand(item.Id, Guid.NewGuid()), Ct));
    }

    [Fact]
    public async Task Handle_NoteOnAnotherItem_ReturnsFalse()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var other = await ItemNoteTestData.AddItemAsync(_db, name: "Other");
        var note = await ItemNoteTestData.AddNoteAsync(_db, item.Id, DateTimeOffset.UtcNow.AddDays(-1));

        Assert.False(await _sut.Handle(new DeleteItemNoteCommand(other.Id, note.Id), Ct));
        Assert.Single(_db.ItemNotes);
    }

    [Fact]
    public async Task Handle_Deletes_AndAuditsWithSubjectAsDescription()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var note = await ItemNoteTestData.AddNoteAsync(_db, item.Id, DateTimeOffset.UtcNow.AddDays(-1),
            subject: "Oil change", body: "5W-30\nsecond line");

        var result = await _sut.Handle(new DeleteItemNoteCommand(item.Id, note.Id), Ct);

        Assert.True(result);
        Assert.Empty(_db.ItemNotes);
        _audit.Received(1).Deleted("InventoryNote", note.Id, "Oil change", item.Id);
    }

    [Fact]
    public async Task Handle_NoSubject_DescriptionIsFirstLineOfBody()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var note = await ItemNoteTestData.AddNoteAsync(_db, item.Id, DateTimeOffset.UtcNow.AddDays(-1),
            body: "Needs a new blade \r\nOrdered one from the store");

        await _sut.Handle(new DeleteItemNoteCommand(item.Id, note.Id), Ct);

        _audit.Received(1).Deleted("InventoryNote", note.Id, "Needs a new blade", item.Id);
    }
}
